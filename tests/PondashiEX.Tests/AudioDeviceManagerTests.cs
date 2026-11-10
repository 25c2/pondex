using PondashiEX.Audio;

namespace PondashiEX.Tests;

public sealed class AudioDeviceManagerTests
{
    [Fact]
    public async Task SlowProbe_IsSharedAcrossCallersAndTimeoutDoesNotStartAnotherWorker()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var manager = new AudioDeviceManager(() =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) { throw new TimeoutException(); }
            return new AudioDeviceSnapshot(Devices, new(0, "usb"));
        });
        var first = manager.CaptureSnapshotAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<TimeoutException>(() => first.WaitAsync(TimeSpan.FromMilliseconds(1)));
            Assert.Same(first, manager.CaptureSnapshotAsync());
            Assert.Equal(1, calls);
        }
        finally { release.Set(); }
        Assert.Same(Devices, (await first).Devices);
    }

    [Fact]
    public void DefaultBinding_PinsPhysicalNumberAndDetectsEndpointOrNumberChanges()
    {
        var original = new AudioDeviceSnapshot(Devices, new(0, "usb-endpoint"));
        var binding = Assert.IsType<AudioOutputBinding>(AudioOutputBinding.Create("default", original));
        Assert.Equal(0, binding.Device.DeviceNumber);
        Assert.True(binding.Matches(original));
        Assert.False(binding.Matches(original with { DefaultOutput = new(3, "hdmi-endpoint") }));
        Assert.False(binding.Matches(original with { DefaultOutput = new(0, "replacement-endpoint") }));
        Assert.False(binding.Matches(original with { DefaultOutput = null }));
        Assert.False(binding.Matches(original with { Devices = [AudioDeviceInfo.Default] }));
        Assert.Null(AudioOutputBinding.Create("default", original with { DefaultOutput = null }));
    }

    [Fact]
    public void SpecificBinding_IgnoresDefaultChangesButStopsOnRemovalOrRenumbering()
    {
        var original = new AudioDeviceSnapshot(Devices, new(3, "hdmi"));
        var binding = Assert.IsType<AudioOutputBinding>(AudioOutputBinding.Create(Devices[1].PersistentId, original));
        Assert.True(binding.Matches(original with { DefaultOutput = new(0, "usb") }));
        Assert.False(binding.Matches(original with { Devices = [AudioDeviceInfo.Default, Devices[1] with { DeviceNumber = 2 }] }));
        Assert.False(binding.Matches(original with { Devices = [AudioDeviceInfo.Default] }));
        // Reconnection does not mutate/reselect the binding; the UI remains stopped until selection.
        Assert.True(binding.Matches(original));
    }

    private static readonly AudioDeviceInfo[] Devices =
    [
        AudioDeviceInfo.Default,
        new AudioDeviceInfo(0, "USB Audio", "waveout-name:11111111-1111-1111-1111-111111111111"),
        new AudioDeviceInfo(3, "HDMI", "waveout-name:33333333-3333-3333-3333-333333333333")
    ];

    [Fact]
    public void FindConfiguredDevice_RejectsAmbiguousPersistentIdentity()
    {
        var first = Devices[1];
        Assert.Null(AudioDeviceManager.FindConfiguredDevice([first, first with { DeviceNumber = 2 }], first.PersistentId));
    }

    [Fact]
    public void FindConfiguredDevice_UsesPersistentIdAfterDeviceOrderChanges()
    {
        var result = AudioDeviceManager.FindConfiguredDevice(
            Devices,
            "waveout-name:33333333-3333-3333-3333-333333333333");

        Assert.Equal(3, result?.DeviceNumber);
    }

    [Fact]
    public void FindConfiguredDevice_AcceptsLegacyNumericSetting()
    {
        var result = AudioDeviceManager.FindConfiguredDevice(Devices, "0");

        Assert.Equal("USB Audio", result?.Name);
    }

    [Fact]
    public void FindConfiguredDevice_ReturnsNullForMissingDevice()
    {
        var result = AudioDeviceManager.FindConfiguredDevice(Devices, "waveout-name:missing");

        Assert.Null(result);
    }
}
