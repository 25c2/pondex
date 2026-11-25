using System.Security.Cryptography;

namespace pondex.Project;

internal readonly record struct ProjectSaveDestination(string Path, string? Revision)
{
    internal static Task<ProjectSaveDestination> CaptureAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var resolved = ProjectPath.GetSafeFullPath(path, "保存先パス");
            return new ProjectSaveDestination(resolved, ReadRevision(resolved));
        }, cancellationToken);

    internal void VerifyPath(string actualPath)
    {
        if (!string.Equals(Path, actualPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new IOException("保存先の参照先が変わりました。保存先を選び直してください。");
        }
    }

    internal void VerifyRevision()
    {
        if (!string.Equals(Revision, ReadRevision(Path), StringComparison.Ordinal))
        {
            throw new IOException("別のウィンドウまたはアプリが保存先を変更しました。保存先の内容を確認し、保存先を選び直してください。");
        }
    }

    private static string? ReadRevision(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            if (file.Length > ProjectLimits.MaxFileBytes)
            {
                throw new IOException("上書き先がプロジェクトの最大サイズを超えています。別の保存先を選んでください。");
            }
            return Convert.ToHexString(SHA256.HashData(file));
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException) { return null; }
    }
}
