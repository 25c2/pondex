using PondashiEX.Diagnostics;

namespace PondashiEX.Tests;

public sealed class SoakSelfTestTests
{
    [Fact]
    public void TryParseDuration_ParsesInvariantFractionalMinutes()
    {
        var found = SoakSelfTest.TryParseDuration(
            ["--soak-test-minutes=1.5"],
            out var duration);

        Assert.True(found);
        Assert.Equal(TimeSpan.FromSeconds(90), duration);
    }

    [Theory]
    [InlineData("--soak-test-minutes=0")]
    [InlineData("--soak-test-minutes=1441")]
    [InlineData("--soak-test-minutes=invalid")]
    public void TryParseDuration_RejectsUnsafeValue(string argument)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SoakSelfTest.TryParseDuration([argument], out _));
    }
}
