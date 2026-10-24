using System.Diagnostics;
using PondashiEX.Audio;

namespace PondashiEX.Tests;

public sealed class PlaybackStartDiagnosticsTests
{
    [Fact]
    public void RecentResultsAreBoundedAndOldSessionsCannotCompleteIntoNewSession()
    {
        var diagnostics = new PlaybackStartDiagnostics { Enabled = true };
        var id = Guid.NewGuid();
        var stale = diagnostics.Begin(id);
        diagnostics.Enabled = false;
        diagnostics.Enabled = true;
        Assert.Null(diagnostics.Complete(id, stale, Stopwatch.GetTimestamp(), Stopwatch.GetTimestamp()));
        for (var index = 0; index < 150; index++)
        {
            var request = diagnostics.Begin(id);
            Assert.NotNull(diagnostics.Complete(id, request, Stopwatch.GetTimestamp(), Stopwatch.GetTimestamp()));
        }
        var recent = diagnostics.Snapshot();
        Assert.Equal(128, recent.Count);
        Assert.Equal(23, recent[0].Attempt);
        Assert.Equal(150, recent[^1].Attempt);
    }
}
