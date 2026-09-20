namespace PondashiEX.Project;

public static class ProjectPath
{
    public static string ToStoredPath(string projectFilePath, string audioFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(audioFilePath);

        var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectFilePath))
            ?? throw new ArgumentException("プロジェクトの保存先フォルダを特定できません。", nameof(projectFilePath));
        var relativePath = Path.GetRelativePath(projectDirectory, Path.GetFullPath(audioFilePath));
        return relativePath.Replace(Path.DirectorySeparatorChar, '/');
    }

    public static string Resolve(string projectFilePath, string storedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(storedPath);

        var osPath = storedPath.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(osPath))
        {
            return Path.GetFullPath(osPath);
        }

        var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectFilePath))
            ?? throw new ArgumentException("プロジェクトの保存先フォルダを特定できません。", nameof(projectFilePath));
        return Path.GetFullPath(Path.Combine(projectDirectory, osPath));
    }
}

