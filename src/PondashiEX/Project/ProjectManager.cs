namespace PondashiEX.Project;

public sealed class ProjectManager : IDisposable
{
    private readonly ProjectSerializer _serializer;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _stateLock = new();
    private ProjectModel _current = new();
    private string? _currentPath;
    private bool _isDirty;
    private long _stateVersion;
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

    public event EventHandler? ProjectChanged;

    public ProjectSaveResult? LastSaveResult { get; private set; }

    public void New()
    {
        Interlocked.Increment(ref _replaceRequestId);
        lock (_stateLock)
        {
            _current = new ProjectModel();
            _currentPath = null;
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
            var candidate = await _serializer.LoadAsync(fullPath, cancellationToken).ConfigureAwait(true);
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

                _current = candidate;
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

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(true);
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
                stateVersion = _stateVersion;
                snapshot = _serializer.CreateSnapshot(current);
            }

            LastSaveResult = null;
            LastSaveResult = await _serializer.SaveAsync(currentPath, snapshot, cancellationToken).ConfigureAwait(true);

            var changed = false;
            lock (_stateLock)
            {
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

    public async Task SaveAsAsync(string path, CancellationToken cancellationToken = default)
    {
        var newPath = ProjectPath.GetSafeFullPath(path, "保存先パス");
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            ProjectModel current;
            ProjectModel snapshot;
            string? oldPath;
            long stateVersion;
            lock (_stateLock)
            {
                current = _current;
                oldPath = _currentPath;
                stateVersion = _stateVersion;
                snapshot = _serializer.CreateSnapshot(current);
            }

            foreach (var sound in snapshot.Sounds)
            {
                var resolvedPath = ResolveSoundPath(sound, oldPath);
                sound.File = ProjectPath.ToStoredPath(newPath, resolvedPath);
            }

            LastSaveResult = null;
            LastSaveResult = await _serializer.SaveAsync(newPath, snapshot, cancellationToken).ConfigureAwait(true);

            var changed = false;
            lock (_stateLock)
            {
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
            _current.Sounds.Add(sound);
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

    public void MarkDirty()
    {
        lock (_stateLock)
        {
            MarkDirtyLocked();
        }

        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => _operationGate.Dispose();

    private static string ResolveSoundPath(SoundDefinition sound, string? projectPath)
    {
        if (projectPath is null)
        {
            return ProjectPath.GetSafeFullPath(sound.File, "音源パス");
        }

        return ProjectPath.Resolve(projectPath, sound.File);
    }

    private void MarkDirtyLocked()
    {
        _isDirty = true;
        _stateVersion++;
    }
}
