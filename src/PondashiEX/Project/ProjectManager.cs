namespace PondashiEX.Project;

public sealed class ProjectManager
{
    private readonly ProjectSerializer _serializer;

    public ProjectManager(ProjectSerializer serializer)
    {
        _serializer = serializer;
    }

    public ProjectModel Current { get; private set; } = new();

    public string? CurrentPath { get; private set; }

    public bool IsDirty { get; private set; }

    public event EventHandler? ProjectChanged;

    public void New()
    {
        Current = new ProjectModel();
        CurrentPath = null;
        IsDirty = false;
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        Current = await _serializer.LoadAsync(path, cancellationToken);
        CurrentPath = Path.GetFullPath(path);
        IsDirty = false;
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentPath is null)
        {
            throw new InvalidOperationException("保存先が未設定です。Save Asを使用してください。");
        }

        await _serializer.SaveAsync(CurrentPath, Current, cancellationToken);
        IsDirty = false;
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveAsAsync(string path, CancellationToken cancellationToken = default)
    {
        var newPath = Path.GetFullPath(path);
        var originalFiles = Current.Sounds.Select(sound => sound.File).ToArray();
        var resolvedFiles = Current.Sounds
            .Select(sound => ResolveSoundPath(sound))
            .ToArray();

        try
        {
            for (var index = 0; index < Current.Sounds.Count; index++)
            {
                Current.Sounds[index].File = ProjectPath.ToStoredPath(newPath, resolvedFiles[index]);
            }

            await _serializer.SaveAsync(newPath, Current, cancellationToken);
            CurrentPath = newPath;
            IsDirty = false;
            ProjectChanged?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            for (var index = 0; index < Current.Sounds.Count; index++)
            {
                Current.Sounds[index].File = originalFiles[index];
            }

            throw;
        }
    }

    public SoundDefinition AddSound(string audioFilePath)
    {
        var storedPath = CurrentPath is null
            ? Path.GetFullPath(audioFilePath)
            : ProjectPath.ToStoredPath(CurrentPath, audioFilePath);
        var sound = new SoundDefinition
        {
            Id = Guid.NewGuid(),
            Name = Path.GetFileNameWithoutExtension(audioFilePath),
            File = storedPath
        };
        Current.Sounds.Add(sound);
        MarkDirty();
        return sound;
    }

    public void ReplaceSoundFile(SoundDefinition sound, string audioFilePath)
    {
        ArgumentNullException.ThrowIfNull(sound);
        ArgumentException.ThrowIfNullOrWhiteSpace(audioFilePath);
        sound.File = CurrentPath is null
            ? Path.GetFullPath(audioFilePath)
            : ProjectPath.ToStoredPath(CurrentPath, audioFilePath);
        MarkDirty();
    }

    public string ResolveSoundPath(SoundDefinition sound)
    {
        ArgumentNullException.ThrowIfNull(sound);
        if (CurrentPath is null || Path.IsPathRooted(sound.File))
        {
            return Path.GetFullPath(sound.File);
        }

        return ProjectPath.Resolve(CurrentPath, sound.File);
    }

    public void MarkDirty()
    {
        IsDirty = true;
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }
}
