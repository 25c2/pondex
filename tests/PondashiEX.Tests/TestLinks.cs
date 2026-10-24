using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PondashiEX.Tests;

internal static class TestLinks
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);

    internal static void CreateJunction(string link, string target)
    {
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "/c", "mklink", "/J", Path.GetFullPath(link), Path.GetFullPath(target) })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(10_000), "ジャンクション作成がタイムアウトしました。");
        Assert.True(process.ExitCode == 0, output + error);
    }
}
