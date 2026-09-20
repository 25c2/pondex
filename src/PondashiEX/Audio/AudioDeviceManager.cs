using NAudio.Wave;

namespace PondashiEX.Audio;

public sealed record AudioDeviceInfo(int DeviceNumber, string Name, string PersistentId)
{
    public static AudioDeviceInfo Default { get; } = new(-1, "Windows 既定の出力", "default");
}

public sealed class AudioDeviceManager
{
    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices()
    {
        var devices = new List<AudioDeviceInfo> { AudioDeviceInfo.Default };
        for (var index = 0; index < WaveOut.DeviceCount; index++)
        {
            try
            {
                var capabilities = WaveOut.GetCapabilities(index);
                devices.Add(new AudioDeviceInfo(
                    index,
                    capabilities.ProductName,
                    CreatePersistentId(capabilities)));
            }
            catch
            {
                // 列挙途中で消失したデバイスだけを無視し、他の候補は残す。
            }
        }

        return devices;
    }

    public static AudioDeviceInfo? FindConfiguredDevice(
        IReadOnlyList<AudioDeviceInfo> devices,
        string? configuredId)
    {
        ArgumentNullException.ThrowIfNull(devices);
        if (string.IsNullOrWhiteSpace(configuredId)
            || string.Equals(configuredId, AudioDeviceInfo.Default.PersistentId, StringComparison.OrdinalIgnoreCase))
        {
            return devices.FirstOrDefault(device => device.DeviceNumber == -1) ?? AudioDeviceInfo.Default;
        }

        var persistentMatch = devices.FirstOrDefault(
            device => string.Equals(device.PersistentId, configuredId, StringComparison.OrdinalIgnoreCase));
        if (persistentMatch is not null)
        {
            return persistentMatch;
        }

        // v1.0 preview builds stored the volatile WaveOut index. Keep those projects readable.
        return int.TryParse(
            configuredId,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out var legacyNumber)
            ? devices.FirstOrDefault(device => device.DeviceNumber == legacyNumber)
            : null;
    }

    private static string CreatePersistentId(WaveOutCapabilities capabilities)
    {
        if (capabilities.NameGuid != Guid.Empty)
        {
            return $"waveout-name:{capabilities.NameGuid:D}";
        }

        return $"waveout-product:{capabilities.ManufacturerGuid:D}:{capabilities.ProductGuid:D}:{capabilities.ProductName}";
    }
}
