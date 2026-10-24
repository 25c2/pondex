using NAudio.Wave;

namespace PondashiEX.Audio;

internal interface IAudioOutput : IDisposable
{
    event EventHandler<StoppedEventArgs>? PlaybackStopped;

    NAudio.Wave.PlaybackState PlaybackState { get; }

    // Null is for synchronous/test sinks that have no hardware queue.
    long? PlayedSamples => null;

    bool HasConsumedSamples(long sampleCount) => PlayedSamples is not { } played || played >= sampleCount;

    void Play();

    void Stop();
}

internal sealed class WaveOutAudioOutput : IAudioOutput
{
    private readonly WaveOutEvent _output;

    public WaveOutAudioOutput(int deviceNumber, ISampleProvider provider)
    {
        _output = new WaveOutEvent
        {
            DeviceNumber = deviceNumber,
            DesiredLatency = 120,
            NumberOfBuffers = 3
        };
        _output.PlaybackStopped += (_, eventArgs) => PlaybackStopped?.Invoke(this, eventArgs);
        _output.Init(provider);
    }

    public event EventHandler<StoppedEventArgs>? PlaybackStopped;

    public NAudio.Wave.PlaybackState PlaybackState => _output.PlaybackState;

    public bool HasConsumedSamples(long sampleCount)
    {
        var targetBytes = unchecked((uint)(sampleCount * (_output.OutputWaveFormat.BitsPerSample / 8)));
        return HasPassedPosition(unchecked((uint)_output.GetPosition()), targetBytes);
    }

    // The target is at most one queued buffer ahead. Modular comparison handles
    // waveOut's 4-GiB byte cursor wrap without a permanent polling timer.
    internal static bool HasPassedPosition(uint position, uint target) => unchecked((int)(position - target)) >= 0;

    public void Play() => _output.Play();

    public void Stop() => _output.Stop();

    public void Dispose() => _output.Dispose();
}

