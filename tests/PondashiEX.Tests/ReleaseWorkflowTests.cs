using System.Text.RegularExpressions;

namespace PondashiEX.Tests;

public sealed class ReleaseWorkflowTests
{
    [Fact]
    public void TagIsReadAsEnvironmentDataNeverInterpolatedIntoPowerShellSource()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "PondashiEX.sln"))) { root = root.Parent; }
        Assert.NotNull(root);
        var workflow = File.ReadAllText(Path.Combine(root.FullName, ".github/workflows/release.yml"));
        Assert.DoesNotContain("${{ github.ref_name }}", workflow, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Count(workflow, Regex.Escape("$tag = $env:GITHUB_REF_NAME")));
        var pattern = Regex.Match(workflow, @"\$pattern = '([^']+)'").Groups[1].Value;
        Assert.NotEmpty(pattern);
        Assert.Matches(pattern, "v1.0-beta.5");
        Assert.Matches(pattern, "v26.10.24");
        // Deliberately a literal string, not a PowerShell script or a real Git tag.
        Assert.DoesNotMatch(pattern, "v1.0-$(Write-Output-injected)");
    }
}
