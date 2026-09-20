using NAudio.Wave;

namespace PondashiEX.Audio;

internal interface IAudioOutput : IDisposable
{
    event EventHandler<StoppedEventArgs>? PlaybackStopped;

    NAudio.Wave.PlaybackState PlaybackState { get; }

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

    public void Play() => _output.Play();

    public void Stop() => _output.Stop();

    public void Dispose() => _output.Dispose();
}

