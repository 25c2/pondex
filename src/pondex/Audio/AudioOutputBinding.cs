namespace pondex.Audio;

public sealed record DefaultOutputIdentity(int DeviceNumber, string EndpointId);

public sealed record AudioDeviceSnapshot(IReadOnlyList<AudioDeviceInfo> Devices, DefaultOutputIdentity? DefaultOutput);

// The default selection is resolved to a physical WinMM number once, never opened as WAVE_MAPPER.
public sealed record AudioOutputBinding(string ConfiguredId, AudioDeviceInfo Device, string? DefaultEndpointId)
{
    public static AudioOutputBinding? Create(string configuredId, AudioDeviceSnapshot snapshot)
    {
        var selected = AudioDeviceManager.FindConfiguredDevice(snapshot.Devices, configuredId);
        if (selected is null) { return null; }
        if (selected.DeviceNumber >= 0) { return new(configuredId, selected, null); }
        if (snapshot.DefaultOutput is not { DeviceNumber: >= 0 } preferred || string.IsNullOrWhiteSpace(preferred.EndpointId)) { return null; }
        var physical = snapshot.Devices.SingleOrDefault(device => device.DeviceNumber == preferred.DeviceNumber);
        return physical is null ? null : new(configuredId, physical, preferred.EndpointId);
    }

    public bool Matches(AudioDeviceSnapshot snapshot)
    {
        var current = Create(ConfiguredId, snapshot);
        return current is not null && current.Device.DeviceNumber == Device.DeviceNumber
            && string.Equals(current.Device.PersistentId, Device.PersistentId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(current.DefaultEndpointId, DefaultEndpointId, StringComparison.OrdinalIgnoreCase);
    }
}
