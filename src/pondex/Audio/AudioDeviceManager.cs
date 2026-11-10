using NAudio.Wave;
using System.Runtime.InteropServices;

namespace pondex.Audio;

public sealed record AudioDeviceInfo(int DeviceNumber, string Name, string PersistentId)
{
    public static AudioDeviceInfo Default { get; } = new(-1, "Windows既定", "default");
}

public sealed class AudioDeviceManager
{
    private readonly Func<AudioDeviceSnapshot>? _captureSnapshot;
    private readonly object _captureSync = new();
    private Task<AudioDeviceSnapshot>? _captureTask;

    public AudioDeviceManager() { }

    internal AudioDeviceManager(Func<AudioDeviceSnapshot> captureSnapshot) => _captureSnapshot = captureSnapshot;

    public AudioDeviceSnapshot CaptureSnapshot() => _captureSnapshot?.Invoke()
        ?? new AudioDeviceSnapshot(GetOutputDevices(), QueryDefaultOutput());

    public Task<AudioDeviceSnapshot> CaptureSnapshotAsync()
    {
        lock (_captureSync)
        {
            // A driver call cannot be forcibly cancelled. Reuse it while pending,
            // including after a caller timeout, instead of accumulating blocked workers.
            if (_captureTask is null || _captureTask.IsCompleted) { _captureTask = Task.Run(CaptureSnapshot); }
            return _captureTask;
        }
    }

    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices()
    {
        if (_captureSnapshot is not null) { return _captureSnapshot().Devices; }
        var devices = new List<AudioDeviceInfo> { AudioDeviceInfo.Default };
        for (var index = 0; index < WaveOutEvent.DeviceCount; index++)
        {
            try
            {
                var capabilities = WaveOutEvent.GetCapabilities(index);
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

        var matches = devices.Where(
            device => string.Equals(device.PersistentId, configuredId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length > 0)
        {
            // Identical legacy capabilities are not enough to select safely.
            return matches.Length == 1 ? matches[0] : null;
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

    private static DefaultOutputIdentity? QueryDefaultOutput()
    {
        if (!OperatingSystem.IsWindows()) { return null; }
        // Microsoft mmddk.h: DRVM_MAPPER_PREFERRED_GET, DRV_QUERYFUNCTIONINSTANCEIDSIZE / ID.
        const uint preferredGet = 0x2015, querySize = 0x0812, queryId = 0x0811;
        try
        {
            if (WaveOutMessagePair(new IntPtr(-1), preferredGet, out var number, out _) != 0
                || number >= WaveOutEvent.DeviceCount) { return null; }
            var device = new IntPtr(number);
            if (WaveOutMessageSize(device, querySize, out var size, UIntPtr.Zero) != 0
                || size < 2 || size > 64 * 1024) { return null; }
            // Reserve UTF-16 storage even for drivers reporting a character count.
            // Standard drivers report bytes; passing their reported size remains unchanged.
            var bufferBytes = checked((int)size * 2);
            var buffer = Marshal.AllocHGlobal(bufferBytes);
            try
            {
                // Zero initialization bounds PtrToStringUni even on a faulty driver response.
                Marshal.Copy(new byte[bufferBytes], 0, buffer, bufferBytes);
                if (WaveOutMessageBuffer(device, queryId, buffer, new UIntPtr(size)) != 0) { return null; }
                var id = Marshal.PtrToStringUni(buffer, (int)size)?.TrimEnd('\0');
                return string.IsNullOrWhiteSpace(id) ? null : new((int)number, id);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        catch { return null; } // Unsupported/removed drivers fail closed; explicit selection remains possible.
    }

    [DllImport("winmm.dll", EntryPoint = "waveOutMessage", ExactSpelling = true)]
    private static extern uint WaveOutMessagePair(IntPtr device, uint message, out uint first, out uint second);

    [DllImport("winmm.dll", EntryPoint = "waveOutMessage", ExactSpelling = true)]
    private static extern uint WaveOutMessageSize(IntPtr device, uint message, out uint size, UIntPtr unused);

    [DllImport("winmm.dll", EntryPoint = "waveOutMessage", ExactSpelling = true)]
    private static extern uint WaveOutMessageBuffer(IntPtr device, uint message, IntPtr buffer, UIntPtr size);
}
