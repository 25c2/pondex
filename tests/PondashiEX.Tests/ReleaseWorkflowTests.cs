using System.Text.RegularExpressions;

namespace PondashiEX.Tests;

public sealed class ReleaseWorkflowTests
{
    [Fact]
    public void TagIsReadAsEnvironmentDataNeverInterpolatedIntoPowerShellSource()
    {
        var workflow = ReadWorkflow();
        Assert.DoesNotContain("${{ github.ref_name }}", workflow, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Count(workflow, Regex.Escape("$tag = $env:GITHUB_REF_NAME")));
        var pattern = Regex.Match(workflow, @"\$pattern = '([^']+)'").Groups[1].Value;
        Assert.NotEmpty(pattern);
        Assert.Matches(pattern, "v1.0-beta.5");
        Assert.Matches(pattern, "v26.10.24");
        // Deliberately a literal string, not a PowerShell script or a real Git tag.
        Assert.DoesNotMatch(pattern, "v1.0-$(Write-Output-injected)");
    }

    [Theory]
    [InlineData("v1.0", "1.0")]
    [InlineData("v1.0.1", "1.0.1")]
    [InlineData("v26.11.06.1", "26.11.06.1")]
    [InlineData("v1.0-alpha.1", "1.0-alpha.1")]
    [InlineData("v1.0-beta.6", "1.0-beta.6")]
    [InlineData("v1.0-rc.1", "1.0-rc.1")]
    [InlineData("v1.0-rc", "1.0-rc")]
    [InlineData("v1.0-preview.2", "1.0-preview.2")]
    [InlineData("v1.0-dev.20261106", "1.0-dev.20261106")]
    [InlineData("v26.11.06.1-rc.2", "26.11.06.1-rc.2")]
    public void ReleaseTag_AcceptsStableAndPrereleaseVersions(string tag, string expectedVersion)
    {
        var pattern = Regex.Match(ReadWorkflow(), @"\$pattern = '([^']+)'").Groups[1].Value;
        Assert.NotEmpty(pattern);
        var match = Regex.Match(tag, pattern);
        Assert.True(match.Success, $"リリースタグが拒否されました: {tag}");
        Assert.Equal(expectedVersion, match.Groups["version"].Value);
    }

    [Theory]
    [InlineData("v1.0rc1")]
    [InlineData("v1.0-rc..1")]
    [InlineData("v1.0-rc_1")]
    [InlineData("v1.0-")]
    public void ReleaseTag_RejectsMalformedPrereleaseVersions(string tag)
    {
        var pattern = Regex.Match(ReadWorkflow(), @"\$pattern = '([^']+)'").Groups[1].Value;
        Assert.NotEmpty(pattern);
        Assert.DoesNotMatch(pattern, tag);
    }

    private static string ReadWorkflow()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "PondashiEX.sln"))) { root = root.Parent; }
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root.FullName, ".github/workflows/release.yml"));
    }
}
