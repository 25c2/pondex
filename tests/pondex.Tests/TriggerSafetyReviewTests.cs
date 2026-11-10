using pondex.Project;
using pondex.Trigger;

namespace pondex.Tests;

public sealed class TriggerSafetyReviewTests
{
    private static readonly string[] DispatchOperations = ["suspend", "project", "reset"];
    private static readonly DateTimeOffset Noon = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ClockBoundary_PreservesSubMillisecondPrecision()
    {
        var engine = new TriggerEngine();
        engine.SetTriggers([new TriggerDefinition { TriggerType = TriggerType.ClockTime,
            Parameters = new TriggerParameters { ClockTime = new TimeOnly(12, 0).Add(TimeSpan.FromTicks(1)) } }]);
        Assert.Empty(engine.Tick(Noon.AddTicks(-1), TimeSpan.Zero));
        Assert.Empty(engine.Tick(Noon, TimeSpan.Zero));
        Assert.Single(engine.Tick(Noon.AddTicks(1), TimeSpan.Zero));
        Assert.Empty(engine.Tick(Noon.AddTicks(2), TimeSpan.Zero));
    }

    [Theory]
    [InlineData(20.999, 1)]
    [InlineData(21, 0)]
    [InlineData(21.001, 0)]
    public void Resume_ConsumesPastAndBoundaryConditionsButKeepsFuture(double resumedAt, int expectedFuture)
    {
        var engine = new TriggerEngine();
        var past = StopwatchTrigger(10);
        var future = StopwatchTrigger(21);
        var clock = new TriggerDefinition
        {
            TriggerType = TriggerType.ClockTime,
            Parameters = new TriggerParameters { ClockTime = new TimeOnly(12, 0, 21) }
        };
        engine.SetTriggers([past, future, clock]);
        engine.Tick(Noon, TimeSpan.Zero);
        Assert.Equal(past, Assert.Single(engine.Tick(Noon.AddSeconds(10), TimeSpan.FromSeconds(10))));
        engine.Suspend();
        Assert.Empty(engine.Tick(Noon.AddSeconds(20), TimeSpan.FromSeconds(20)));
        engine.Resume(Noon.AddSeconds(resumedAt), TimeSpan.FromSeconds(resumedAt), true);
        var fired = engine.Tick(Noon.AddSeconds(22), TimeSpan.FromSeconds(22));
        Assert.Equal(expectedFuture * 2, fired.Count);
        Assert.DoesNotContain(past, fired);
        Assert.Empty(engine.Tick(Noon.AddSeconds(23), TimeSpan.FromSeconds(23)));
        engine.ResetStopwatch();
        Assert.Equal(past, Assert.Single(engine.Tick(Noon.AddSeconds(24), TimeSpan.FromSeconds(10))));
    }

    [Fact]
    public void Zero_FiresOnlyOnStartAfterResetAndNeverOnEmergencyResume()
    {
        var engine = new TriggerEngine();
        engine.SetTriggers([StopwatchTrigger(0)]);
        engine.Suspend();
        engine.Resume(Noon, TimeSpan.Zero, false);
        Assert.Empty(engine.Tick(Noon, TimeSpan.Zero, false));
        Assert.Single(engine.Tick(Noon, TimeSpan.Zero, true));
        engine.Suspend();
        engine.Resume(Noon, TimeSpan.Zero, false);
        Assert.Empty(engine.Tick(Noon, TimeSpan.Zero, false));
        Assert.Empty(engine.Tick(Noon, TimeSpan.Zero, true));
        engine.ResetStopwatch();
        Assert.Empty(engine.Tick(Noon, TimeSpan.Zero, false));
        Assert.Single(engine.Tick(Noon, TimeSpan.Zero, true));
    }

    [Theory]
    [InlineData(TriggerType.SoundEnded)]
    [InlineData(TriggerType.SoundElapsed)]
    public void SoundNotifications_RequireFreshStartAfterEmergency(TriggerType type)
    {
        var time = new ManualTime();
        var engine = new TriggerEngine(time);
        var source = Guid.NewGuid();
        engine.SetTriggers([new TriggerDefinition { TriggerType = type,
            Parameters = new TriggerParameters { SourceSoundId = source, Elapsed = TimeSpan.Zero } }]);
        engine.OnSoundStarted(source);
        engine.OnSoundEnded(source, true);
        engine.Suspend();
        engine.OnSoundStarted(source);
        engine.OnSoundEnded(source, true);
        engine.Resume(Noon, TimeSpan.Zero, false);
        engine.OnSoundEnded(source, true); // A late completion from before suspension.
        Assert.Empty(engine.Tick(Noon, TimeSpan.Zero, false));
        engine.OnSoundStarted(source);
        if (type == TriggerType.SoundEnded) { engine.OnSoundEnded(source, true); }
        Assert.Single(engine.Tick(Noon, TimeSpan.Zero, false));
        Assert.Empty(engine.Tick(Noon, TimeSpan.Zero, false));
    }

    public static IEnumerable<object[]> DispatchCases() =>
        from operation in DispatchOperations
        from kind in Enum.GetValues<TriggerType>()
        select new object[] { operation, kind };

    [Theory]
    [MemberData(nameof(DispatchCases))]
    public async Task DispatchInvalidatedDuringCallback_DropsRemainingBatchAndMarksOldArgsStale(string operation, TriggerType kind)
    {
        var engine = new TriggerEngine();
        var source = Guid.NewGuid();
        TriggerDefinition Create() => new()
        {
            TriggerType = kind,
            Parameters = new TriggerParameters
            { SourceSoundId = source, Elapsed = TimeSpan.Zero, ClockTime = new TimeOnly(12, 0) }
        };
        engine.SetTriggers([Create(), Create()]);
        Assert.Empty(engine.Tick(Noon.AddSeconds(-1), TimeSpan.Zero, false));
        engine.OnSoundStarted(source);
        engine.OnSoundEnded(Guid.NewGuid(), true);
        if (kind == TriggerType.SoundEnded) { engine.OnSoundEnded(source, true); }
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var notifications = 0;
        TriggerFiredEventArgs? captured = null;
        engine.TriggerFired += (_, args) =>
        {
            Interlocked.Increment(ref notifications);
            captured = args;
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
        };
        var tick = Task.Run(() => engine.Tick(Noon, TimeSpan.Zero));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            if (operation == "suspend") { engine.Suspend(); engine.Resume(Noon, TimeSpan.Zero, true); }
            else if (operation == "project") { engine.SetTriggers([]); }
            else { engine.ResetStopwatch(); }
            Assert.False(captured!.IsCurrent);
        }
        finally { release.Set(); }
        await tick;
        Assert.Equal(1, notifications);
    }

    [Fact]
    public void ProjectReplacement_DoesNotCarryOldLatchesOrSoundNotifications()
    {
        var engine = new TriggerEngine();
        var trigger = StopwatchTrigger(0);
        engine.SetTriggers([trigger]);
        Assert.Single(engine.Tick(Noon, TimeSpan.Zero));
        engine.Suspend();
        engine.SetTriggers([trigger]);
        engine.Resume(Noon, TimeSpan.Zero, false);
        Assert.Single(engine.Tick(Noon, TimeSpan.Zero, true));
    }

    private static TriggerDefinition StopwatchTrigger(double seconds) => new()
    {
        TriggerType = TriggerType.StopwatchElapsed,
        Parameters = new TriggerParameters { Elapsed = TimeSpan.FromSeconds(seconds) }
    };

    private sealed class ManualTime : TimeProvider
    {
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => 0;
    }
}
