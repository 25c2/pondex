using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace pondex.Project;

public enum ProjectPathKind
{
    WithinProjectDirectory,
    ExternalLocal,
    RemoteOrDevice
}

public static class ProjectPath
{
    public static string ToStoredPath(string projectFilePath, string audioFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(audioFilePath);

        var projectFullPath = GetSafeFullPath(projectFilePath, "プロジェクトパス");
        var audioFullPath = GetSafeFullPath(audioFilePath, "音源パス");
        var projectDirectory = Path.GetDirectoryName(projectFullPath)
            ?? throw new ArgumentException("プロジェクトの保存先フォルダを特定できません。", nameof(projectFilePath));
        var relativePath = Path.GetRelativePath(projectDirectory, audioFullPath);
        return relativePath.Replace(Path.DirectorySeparatorChar, '/');
    }

    public static string Resolve(string projectFilePath, string storedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(storedPath);

        var projectFullPath = GetSafeFullPath(projectFilePath, "プロジェクトパス");
        EnsureNotRemoteOrDevice(storedPath, "音源パス");
        var osPath = storedPath.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(osPath))
        {
            return GetSafeFullPath(osPath, "音源パス");
        }

        var projectDirectory = Path.GetDirectoryName(projectFullPath)
            ?? throw new ArgumentException("プロジェクトの保存先フォルダを特定できません。", nameof(projectFilePath));
        return GetSafeFullPath(Path.Combine(projectDirectory, osPath), "音源パス");
    }

    public static ProjectPathKind Classify(string projectFilePath, string storedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(storedPath);

        if (IsRemoteOrDevice(storedPath))
        {
            return ProjectPathKind.RemoteOrDevice;
        }

        var projectFullPath = GetSafeFullPath(projectFilePath, "プロジェクトパス");
        return ClassifyResolved(projectFullPath, Resolve(projectFullPath, storedPath));
    }

    internal static ProjectPathKind ClassifyResolved(string projectFullPath, string resolvedPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectFullPath)
            ?? throw new ArgumentException("プロジェクトの保存先フォルダを特定できません。", nameof(projectFullPath));
        var relativePath = Path.GetRelativePath(projectDirectory, resolvedPath);
        var isOutside = Path.IsPathRooted(relativePath)
            || relativePath.Equals("..", StringComparison.Ordinal)
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
        return isOutside ? ProjectPathKind.ExternalLocal : ProjectPathKind.WithinProjectDirectory;
    }

    public static string GetSafeFullPath(string path, string displayName = "パス")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        EnsureNotRemoteOrDevice(path, displayName);
        return ResolveLocalLinks(Path.GetFullPath(path), displayName, 0);
    }

    internal static FileStream OpenVerifiedRead(string resolvedPath, int bufferSize = 64 * 1024,
        FileOptions options = FileOptions.None)
    {
        var stream = new FileStream(resolvedPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, bufferSize, options);
        try
        {
            var finalPath = OperatingSystem.IsWindows()
                ? GetWindowsFilePath(stream.SafeFileHandle) : GetSafeFullPath(resolvedPath, "音源パス");
            if (!string.Equals(finalPath, resolvedPath,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                throw new InvalidOperationException("開いた音源の参照先が変わりました。「参照…」で選び直してください。");
            }
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static string GetWindowsFilePath(SafeFileHandle handle)
    {
        var buffer = new char[512];
        var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Length, 0);
        if (length >= buffer.Length && length <= 32768)
        {
            buffer = new char[length];
            length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Length, 0);
        }
        if (length == 0 || length >= buffer.Length)
        {
            throw new IOException("開いた音源のパスを確認できません。", new Win32Exception(Marshal.GetLastWin32Error()));
        }
        var finalPath = new string(buffer, 0, (int)length);
        return finalPath.StartsWith(@"\\?\", StringComparison.Ordinal) ? finalPath[4..] : finalPath;
    }

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, [Out] char[] path, uint capacity, uint flags);

    public static bool IsRemoteOrDevice(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalized = path.Replace('/', '\\').TrimStart();
        if (normalized.StartsWith("\\\\", StringComparison.Ordinal)
            || normalized.StartsWith("\\??\\", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsUnc;
    }

    private static void EnsureNotRemoteOrDevice(string path, string displayName)
    {
        if (IsRemoteOrDevice(path))
        {
            throw new InvalidOperationException($"{displayName}にUNCパスまたはデバイスパスは使用できません。");
        }
    }

    private static string ResolveLocalLinks(string fullPath, string displayName, int depth)
    {
        EnsureNotRemoteOrDevice(fullPath, displayName);
        if (depth > 32)
        {
            throw new InvalidOperationException($"{displayName}のリンクが循環しているか、深すぎます。");
        }

        var root = Path.GetPathRoot(fullPath)!;
        if (new DriveInfo(root).DriveType == DriveType.Network)
        {
            throw new InvalidOperationException($"{displayName}にネットワークドライブは使用できません。");
        }

        var parts = fullPath[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        for (var index = 0; index < parts.Length; index++)
        {
            current = Path.Combine(current, parts[index]);
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(current);
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                continue;
            }

            if ((attributes & FileAttributes.ReparsePoint) == 0)
            {
                continue;
            }

            FileSystemInfo entry = (attributes & FileAttributes.Directory) != 0
                ? new DirectoryInfo(current)
                : new FileInfo(current);
            var target = entry.LinkTarget
                ?? throw new InvalidOperationException($"{displayName}に解決できないリンクが含まれます。");
            EnsureNotRemoteOrDevice(target, displayName);
            var targetPath = Path.GetFullPath(target, Path.GetDirectoryName(current)!);
            var remainder = Path.Combine(new[] { targetPath }.Concat(parts.Skip(index + 1)).ToArray());
            return ResolveLocalLinks(remainder, displayName, depth + 1);
        }

        return current;
    }
}
