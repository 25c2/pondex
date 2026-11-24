namespace pondex.Audio;

internal static class AudioDeviceSnapshotChanges
{
    internal static bool Differ(AudioDeviceSnapshot before, AudioDeviceSnapshot after) =>
        before.DefaultOutput != after.DefaultOutput || !GetIdentities(before).SetEquals(GetIdentities(after));

    internal static string Describe(AudioDeviceSnapshot? before, AudioDeviceSnapshot after)
    {
        var previous = before is null ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : GetIdentities(before);
        var current = GetIdentities(after);
        return $"listed={current.Count} default_before={DescribeDefault(before?.DefaultOutput)} default_after={DescribeDefault(after.DefaultOutput)} "
            + $"added=[{string.Join(", ", current.Except(previous, StringComparer.OrdinalIgnoreCase))}] "
            + $"removed=[{string.Join(", ", previous.Except(current, StringComparer.OrdinalIgnoreCase))}]";
    }

    private static HashSet<string> GetIdentities(AudioDeviceSnapshot snapshot) =>
        snapshot.Devices.Where(device => device.DeviceNumber >= 0)
            .Select(device => device.EndpointId is null ? $"{device.DeviceNumber}:{device.SelectionId}"
                : $"{device.DeviceNumber}:{device.SelectionId} ({device.PersistentId})")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string DescribeDefault(DefaultOutputIdentity? identity) =>
        identity is null ? "unavailable" : $"{identity.DeviceNumber}:{identity.EndpointId}";
}
