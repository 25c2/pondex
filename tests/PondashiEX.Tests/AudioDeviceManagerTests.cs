using PondashiEX.Audio;

namespace PondashiEX.Tests;

public sealed class AudioDeviceManagerTests
{
    private static readonly AudioDeviceInfo[] Devices =
    [
        AudioDeviceInfo.Default,
        new AudioDeviceInfo(0, "USB Audio", "waveout-name:11111111-1111-1111-1111-111111111111"),
        new AudioDeviceInfo(3, "HDMI", "waveout-name:33333333-3333-3333-3333-333333333333")
    ];

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
