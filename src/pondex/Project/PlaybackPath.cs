using pondex.Utility;

namespace pondex.Project;

internal readonly record struct PlaybackPath(string File, string? ProjectFile, string? ApprovedPath)
{
    internal string Resolve()
    {
        var path = Resolve(File, ProjectFile);
        if (ApprovedPath is not null && !string.Equals(path, ApprovedPath,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new InvalidOperationException("音源の参照先が変わりました。「参照…」でファイルを選び直してください。");
        }
        return path;
    }

    internal static string Resolve(string file, string? projectFile) => projectFile is null
        ? ProjectPath.GetSafeFullPath(file, "音源パス")
        : ProjectPath.Resolve(projectFile, file);
}
