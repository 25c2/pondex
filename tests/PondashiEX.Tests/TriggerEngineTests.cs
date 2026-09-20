using PondashiEX.Project;
using PondashiEX.Trigger;

namespace PondashiEX.Tests;

public sealed class TriggerEngineTests
{
    [Fact]
    public void Tick_FiresFourTriggerKindsAndAutomaticallyDisablesThem()
    {
        var now = new DateTimeOffset(2026, 9, 19, 19, 30, 0, TimeSpan.FromHours(9));
        var sourceEnded = Guid.NewGuid();
        var sourceElapsed = Guid.NewGuid();
        var triggers = new List<TriggerDefinition>
        {
            CreateTrigger(TriggerType.ClockTime, new TriggerParameters { ClockTime = new TimeOnly(19, 29) }),
            CreateTrigger(TriggerType.StopwatchElapsed, new TriggerParameters { Elapsed = TimeSpan.FromSeconds(5) }),
            CreateTrigger(TriggerType.SoundEnded, new TriggerParameters { SourceSoundId = sourceEnded }),
            CreateTrigger(TriggerType.SoundElapsed, new TriggerParameters
            {
                SourceSoundId = sourceElapsed,
                Elapsed = TimeSpan.FromSeconds(2)
            })
        };
        var timeProvider = new ManualTimeProvider();
        var engine = new TriggerEngine(timeProvider);
        engine.SetTriggers(triggers);
        engine.OnSoundEnded(sourceEnded, naturalEnd: true);
        engine.OnSoundStarted(sourceElapsed);
        timeProvider.Advance(TimeSpan.FromSeconds(3));

        var fired = engine.Tick(now, TimeSpan.FromSeconds(6));

        Assert.Equal(4, fired.Count);
        Assert.All(triggers, trigger => Assert.False(trigger.Enabled));
        Assert.Empty(engine.Tick(now + TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(7)));
    }

    [Fact]
    public void SoundEnded_DoesNotFireForManualStop()
    {
        var sourceId = Guid.NewGuid();
        var trigger = CreateTrigger(
            TriggerType.SoundEnded,
            new TriggerParameters { SourceSoundId = sourceId });
        var engine = new TriggerEngine();
        engine.SetTriggers([trigger]);

        engine.OnSoundEnded(sourceId, naturalEnd: false);

        Assert.Empty(engine.Tick(DateTimeOffset.Now, TimeSpan.Zero));
        Assert.True(trigger.Enabled);
    }

    [Fact]
    public void ClockTime_UsesClockComponentsFromProvidedOffset()
    {
        var trigger = CreateTrigger(
            TriggerType.ClockTime,
            new TriggerParameters { ClockTime = new TimeOnly(19, 29) });
        var engine = new TriggerEngine(new ManualTimeProvider());
        engine.SetTriggers([trigger]);
        var nowInTokyo = new DateTimeOffset(2026, 9, 19, 19, 30, 0, TimeSpan.FromHours(9));

        var fired = engine.Tick(nowInTokyo, TimeSpan.Zero);

        Assert.Single(fired);
        Assert.False(trigger.Enabled);
    }

    private static TriggerDefinition CreateTrigger(TriggerType type, TriggerParameters parameters) => new()
    {
        Enabled = true,
        TriggerType = type,
        Parameters = parameters,
        Action = TriggerAction.Play,
        TargetSoundId = Guid.NewGuid()
    };

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }
}
