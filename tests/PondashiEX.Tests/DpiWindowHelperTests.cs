using System.Drawing;
using PondashiEX.UI;

namespace PondashiEX.Tests;

public sealed class DpiWindowHelperTests
{
    [Fact]
    public void CalculateFittedBounds_ConstrainsOversizedWindowToWorkingArea()
    {
        var workingArea = new Rectangle(0, 0, 1_280, 720);
        var desired = new Rectangle(-50, -30, 2_360, 1_520);

        var result = DpiWindowHelper.CalculateFittedBounds(desired, workingArea);

        Assert.Equal(workingArea, result);
    }

    [Fact]
    public void CalculateFittedBounds_KeepsNormalWindowInsideOffsetMonitor()
    {
        var workingArea = new Rectangle(1_920, 40, 1_920, 1_040);
        var desired = new Rectangle(3_700, 1_000, 800, 600);

        var result = DpiWindowHelper.CalculateFittedBounds(desired, workingArea);

        Assert.Equal(new Rectangle(3_040, 480, 800, 600), result);
    }
}
