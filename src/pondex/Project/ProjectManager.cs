using System.Security.Cryptography;
using System.Text;
using pondex.Utility;

namespace pondex.Project;

public sealed class ProjectManager : IDisposable
{
    private readonly ProjectSerializer _serializer;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _stateLock = new();
    private ProjectModel _current = new();
    private string? _currentPath;
    private bool _isDirty;
    private string? _protectedSourcePath;
    private long _stateVersion;
    private long _shortcutBindingsVersion;

    internal long ShortcutBindingsVersion => Interlocked.Read(ref _shortcutBindingsVersion);
    private long _replaceRequestId;

    public ProjectManager(ProjectSerializer serializer)
    {
        _serializer = serializer;
    }

    public ProjectModel Current
    {
        get
        {
            lock (_stateLock)
            {
                return _current;
            }
        }
    }

    public string? CurrentPath
    {
        get
        {
            lock (_stateLock)
            {
                return _currentPath;
            }
        }
    }

    public bool IsDirty
    {
        get
        {
            lock (_stateLock)
            {
                return _isDirty;
            }
        }
    }

    public bool RequiresSaveAs
    {
        get
        {
            lock (_stateLock)
            {
                return _protectedSourcePath is not null
                    && string.Equals(_currentPath, _protectedSourcePath, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    public event EventHandler? ProjectChanged;

    public ProjectSaveResult? LastSaveResult { get; private set; }

    public void New()
    {
        Interlocked.Increment(ref _replaceRequestId);
        lock (_stateLock)
        {
            _current = new ProjectModel();
            Interlocked.Increment(ref _shortcutBindingsVersion);
            _currentPath = null;
            _protectedSourcePath = null;
            _isDirty = false;
            _stateVersion++;
        }

        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<bool> OpenAsync(
        string path,
        Func<ProjectModel, string, bool>? authorize = null,
        CancellationToken cancellationToken = default)
    {
        var requestId = Interlocked.Increment(ref _replaceRequestId);
        var fullPath = ProjectPath.GetSafeFullPath(path, "プロジェクトパス");
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            var candidate = await Task.Run(() => _serializer.LoadAsync(fullPath, cancellationToken), cancellationToken).ConfigureAwait(true);
            if (requestId != Interlocked.Read(ref _replaceRequestId)
                || (authorize is not null && !authorize(candidate, fullPath)))
            {
                return false;
            }

            lock (_stateLock)
            {
                if (requestId != Interlocked.Read(ref _replaceRequestId))
                {
                    return false;
                }

                _protectedSourcePath = candidate.LoadWarnings.Count > 0 ? fullPath : null;
                _current = candidate;
                Interlocked.Increment(ref _shortcutBindingsVersion);
                _currentPath = fullPath;
                _isDirty = false;
                _stateVersion++;
            }

            ProjectChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public Task SaveAsync(CancellationToken cancellationToken = default) => SaveAsync(null, cancellationToken);

    internal async Task SaveAsync(OperationTiming? timing, CancellationToken cancellationToken = default)
    {
        using (timing?.Measure("operation_gate"))
        {
            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        }
        try
        {
            ProjectModel current;
            ProjectModel snapshot;
            string currentPath;
            long stateVersion;
            lock (_stateLock)
            {
                current = _current;
                currentPath = _currentPath
                    ?? throw new InvalidOperationException("保存先が未設定です。Save Asを使用してください。");
                EnsureSafeSavePath(currentPath);
                stateVersion = _stateVersion;
                using (timing?.Measure("snapshot")) { snapshot = _serializer.CreateSnapshot(current); }
            }

            LastSaveResult = null;
            LastSaveResult = await SaveWithRevisionAsync(currentPath, snapshot, current.FileRevision, timing, cancellationToken).ConfigureAwait(true);

            var changed = false;
            lock (_stateLock)
            {
                if (ReferenceEquals(_current, current)) { current.FileRevision = LastSaveResult.FileRevision; }
                if (ReferenceEquals(_current, current)
                    && _stateVersion == stateVersion
                    && string.Equals(_currentPath, currentPath, StringComparison.OrdinalIgnoreCase))
                {
                    _isDirty = false;
                    changed = true;
                }
            }

            if (changed)
            {
                ProjectChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public Task SaveAsAsync(string path, CancellationToken cancellationToken = default) => SaveAsAsync(path, null, cancellationToken);

    internal async Task SaveAsAsync(string path, OperationTiming? timing, CancellationToken cancellationToken = default)
    {
        using (timing?.Measure("operation_gate"))
        {
            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        }
        try
        {
            ProjectModel current;
            ProjectModel snapshot;
            string? oldPath;
            long stateVersion;
            Dictionary<Guid, string> approvedPaths;
            string? revision;
            lock (_stateLock)
            {
                current = _current;
                approvedPaths = new Dictionary<Guid, string>(current.ApprovedSoundPaths);
                revision = current.FileRevision;
                oldPath = _currentPath;
                stateVersion = _stateVersion;
                using (timing?.Measure("snapshot")) { snapshot = _serializer.CreateSnapshot(current); }
            }

            string newPath;
            using (timing?.Measure("destination_path"))
            {
                newPath = await Task.Run(() => ProjectPath.GetSafeFullPath(path, "保存先パス"), cancellationToken).ConfigureAwait(true);
            }
            lock (_stateLock) { EnsureSafeSavePath(newPath); }

            using (timing?.Measure("rebase_paths"))
            {
                await Task.Run(() =>
                {
                    foreach (var sound in snapshot.Sounds)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var resolvedPath = approvedPaths.TryGetValue(sound.Id, out var approved)
                            ? approved : ResolveSoundPath(sound, oldPath);
                        sound.File = ProjectPath.ToStoredPath(newPath, resolvedPath);
                    }
                }, cancellationToken).ConfigureAwait(true);
            }

            LastSaveResult = null;
            var sameDestination = string.Equals(newPath, oldPath, StringComparison.OrdinalIgnoreCase);
            LastSaveResult = await SaveWithRevisionAsync(newPath, snapshot,
                sameDestination ? revision : null, timing, cancellationToken).ConfigureAwait(true);

            var changed = false;
            lock (_stateLock)
            {
                if (sameDestination && ReferenceEquals(_current, current)) { current.FileRevision = LastSaveResult.FileRevision; }
                if (ReferenceEquals(_current, current) && _stateVersion == stateVersion)
                {
                    var storedPaths = snapshot.Sounds.ToDictionary(sound => sound.Id, sound => sound.File);
                    foreach (var sound in current.Sounds)
                    {
                        if (storedPaths.TryGetValue(sound.Id, out var storedPath))
                        {
                            sound.File = storedPath;
                        }
                    }

                    _currentPath = newPath;
                    current.FileRevision = LastSaveResult.FileRevision;
                    _isDirty = false;
                    _stateVersion++;
                    changed = true;
                }
            }

            if (changed)
            {
                ProjectChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private Task<ProjectSaveResult> SaveWithRevisionAsync(string path, ProjectModel snapshot, string? expectedRevision,
        OperationTiming? timing, CancellationToken cancellationToken) => Task.Run(() =>
    {
        // Use the canonical destination so separate instances cannot both pass
        // a revision check and then replace the same file.
        var lockId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path)));
        using var gate = new Mutex(false, $"pondex-save-{lockId}");
        var acquired = false;
        try
        {
            using (timing?.Measure("file_gate"))
            {
                try
                {
                    var started = System.Diagnostics.Stopwatch.StartNew();
                    do
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        acquired = gate.WaitOne(100);
                    }
                    while (!acquired && started.Elapsed < TimeSpan.FromSeconds(5));
                }
                catch (AbandonedMutexException) { acquired = true; }
            }
            if (!acquired) { throw new IOException("別のウィンドウが保存中です。時間をおいてやり直してください。"); }
            using (timing?.Measure("revision_check"))
            {
                if (expectedRevision is not null && !MatchesRevision(path, expectedRevision))
                {
                    throw new IOException("別のウィンドウまたはアプリがプロジェクトを変更しました。変更を残すには別の名前で保存してください。");
                }
            }
            return _serializer.SaveAsync(path, snapshot, timing, cancellationToken).GetAwaiter().GetResult();
        }
        finally
        {
            if (acquired) { gate.ReleaseMutex(); }
        }
    }, cancellationToken);

    private static bool MatchesRevision(string path, string expected)
    {
        if (!File.Exists(path)) { return false; }
        using var stream = File.OpenRead(path);
        return stream.Length <= ProjectLimits.MaxFileBytes
            && string.Equals(Convert.ToHexString(SHA256.HashData(stream)), expected, StringComparison.Ordinal);
    }

    private void EnsureSafeSavePath(string path)
    {
        if (string.Equals(path, _protectedSourcePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("読込時に一部の設定を復元できなかったため、元ファイルには上書きできません。別の名前で保存してください。");
        }
    }

    public SoundDefinition AddSound(string audioFilePath)
    {
        SoundDefinition sound;
        lock (_stateLock)
        {
            if (_current.Sounds.Count >= ProjectLimits.MaxSounds)
            {
                throw new InvalidOperationException($"音源は最大{ProjectLimits.MaxSounds}件まで追加できます。");
            }

            var storedPath = _currentPath is null
                ? ProjectPath.GetSafeFullPath(audioFilePath, "音源パス")
                : ProjectPath.ToStoredPath(_currentPath, audioFilePath);
            if (storedPath.Length > ProjectLimits.MaxPathLength)
            {
                throw new InvalidOperationException($"音源パスが長すぎます（上限 {ProjectLimits.MaxPathLength}文字）。");
            }

            var soundName = Path.GetFileNameWithoutExtension(audioFilePath);
            if (soundName.Length > ProjectLimits.MaxNameLength)
            {
                soundName = soundName[..ProjectLimits.MaxNameLength];
            }

            sound = new SoundDefinition
            {
                Id = Guid.NewGuid(),
                Name = soundName,
                File = storedPath
            };
            _current.ApprovedSoundPaths[sound.Id] = ResolveSoundPath(sound, _currentPath);
            _current.Sounds.Add(sound);
            Interlocked.Increment(ref _shortcutBindingsVersion);
            MarkDirtyLocked();
        }

        ProjectChanged?.Invoke(this, EventArgs.Empty);
        return sound;
    }

    public void ReplaceSoundFile(SoundDefinition sound, string audioFilePath)
    {
        ArgumentNullException.ThrowIfNull(sound);
        ArgumentException.ThrowIfNullOrWhiteSpace(audioFilePath);
        lock (_stateLock)
        {
            var storedPath = _currentPath is null
                ? ProjectPath.GetSafeFullPath(audioFilePath, "音源パス")
                : ProjectPath.ToStoredPath(_currentPath, audioFilePath);
            if (storedPath.Length > ProjectLimits.MaxPathLength)
            {
                throw new InvalidOperationException($"音源パスが長すぎます（上限 {ProjectLimits.MaxPathLength}文字）。");
            }

            sound.File = storedPath;
            _current.ApprovedSoundPaths[sound.Id] = ResolveSoundPath(sound, _currentPath);
            MarkDirtyLocked();
        }

        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    public string ResolveSoundPath(SoundDefinition sound)
    {
        ArgumentNullException.ThrowIfNull(sound);
        lock (_stateLock)
        {
            return ResolveSoundPath(sound, _currentPath);
        }
    }

    internal string ResolvePlaybackPath(SoundDefinition sound)
    {
        lock (_stateLock)
        {
            return CreatePlaybackPath(sound).Resolve();
        }
    }

    internal PlaybackPath[] SnapshotPlaybackPaths()
    {
        lock (_stateLock)
        {
            return _current.Sounds.Select(CreatePlaybackPath).ToArray();
        }
    }

    private PlaybackPath CreatePlaybackPath(SoundDefinition sound) =>
        new(sound.File, _currentPath, _current.ApprovedSoundPaths.GetValueOrDefault(sound.Id));

    public void MarkDirty() => MarkDirty(shortcutBindingsChanged: true);

    internal void MarkDirty(bool shortcutBindingsChanged)
    {
        lock (_stateLock)
        {
            if (shortcutBindingsChanged) { Interlocked.Increment(ref _shortcutBindingsVersion); }
            MarkDirtyLocked();
        }

        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => _operationGate.Dispose();

    private static string ResolveSoundPath(SoundDefinition sound, string? projectPath) =>
        PlaybackPath.Resolve(sound.File, projectPath);

    private void MarkDirtyLocked()
    {
        _isDirty = true;
        _stateVersion++;
    }
}
