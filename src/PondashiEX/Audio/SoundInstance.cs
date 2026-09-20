using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using PondashiEX.Project;

namespace PondashiEX.Audio;

internal sealed class SoundInstance : IDisposable
{
    private readonly WaveStream _decoder;
    private readonly PauseSampleProvider _pauseProvider;
    private readonly StereoBalanceSampleProvider _balanceProvider;
    private readonly RampedVolumeSampleProvider _volumeProvider;
    private readonly FadeInOutSampleProvider _fadeProvider;
    private readonly CompletionSampleProvider _completionProvider;
    private bool _disposed;

    private SoundInstance(
        SoundDefinition definition,
        WaveStream decoder,
        PauseSampleProvider pauseProvider,
        StereoBalanceSampleProvider balanceProvider,
        RampedVolumeSampleProvider volumeProvider,
        FadeInOutSampleProvider fadeProvider,
        CompletionSampleProvider completionProvider,
        ISampleProvider output)
    {
        Definition = definition;
        _decoder = decoder;
        _pauseProvider = pauseProvider;
        _balanceProvider = balanceProvider;
        _volumeProvider = volumeProvider;
        _fadeProvider = fadeProvider;
        _completionProvider = completionProvider;
        Output = output;
        State = PlaybackState.Playing;
        StartedAt = DateTimeOffset.Now;
    }

    public SoundDefinition Definition { get; }

    public ISampleProvider Output { get; }

    public PlaybackState State { get; private set; }

    public DateTimeOffset StartedAt { get; }

    public long SamplesRead => _completionProvider.SamplesRead;

    public static SoundInstance Create(
        SoundDefinition definition,
        string path,
        WaveFormat mixerFormat,
        Action completed)
    {
        var decoder = AudioDecoderFactory.Open(path);
        try
        {
            ISampleProvider provider = new LoopProvider(decoder, definition.Loop.Enabled, definition.Loop.StartSample);
            provider = NormalizeChannels(provider);

            if (provider.WaveFormat.SampleRate != mixerFormat.SampleRate)
            {
                provider = new WdlResamplingSampleProvider(provider, mixerFormat.SampleRate);
            }

            if (Math.Abs(definition.Pitch - 1.0f) > 0.001f)
            {
                provider = new SmbPitchShiftingSampleProvider(provider)
                {
                    PitchFactor = Math.Clamp(definition.Pitch, 0.5f, 2.0f)
                };
            }

            var balanceProvider = new StereoBalanceSampleProvider(provider)
            {
                Pan = definition.Pan
            };
            provider = balanceProvider;
            var volumeProvider = new RampedVolumeSampleProvider(provider, definition.Volume);
            provider = volumeProvider;

            var fadeProvider = new FadeInOutSampleProvider(
                provider,
                initiallySilent: definition.FadeIn.Enabled && definition.FadeIn.Value > 0);
            if (definition.FadeIn.Enabled && definition.FadeIn.Value > 0)
            {
                fadeProvider.BeginFadeIn((int)Math.Clamp(definition.FadeIn.Value * 1000, 1, 60_000));
            }
            provider = fadeProvider;

            var pauseProvider = new PauseSampleProvider(provider);
            var completionProvider = new CompletionSampleProvider(pauseProvider, completed);
            return new SoundInstance(
                definition,
                decoder,
                pauseProvider,
                balanceProvider,
                volumeProvider,
                fadeProvider,
                completionProvider,
                completionProvider);
        }
        catch
        {
            decoder.Dispose();
            throw;
        }
    }

    public void Pause()
    {
        if (State != PlaybackState.Playing)
        {
            return;
        }

        _pauseProvider.IsPaused = true;
        State = PlaybackState.Paused;
    }

    public void Resume()
    {
        if (State != PlaybackState.Paused)
        {
            return;
        }

        _pauseProvider.IsPaused = false;
        State = PlaybackState.Playing;
    }

    public void MarkStopped() => State = PlaybackState.Stopped;

    public void BeginFadeOut(TimeSpan duration)
    {
        _fadeProvider.BeginFadeOut((int)Math.Clamp(duration.TotalMilliseconds, 1, 60_000));
        State = PlaybackState.Fading;
    }

    public void BeginVolumeFade(float targetVolume, TimeSpan duration) =>
        _volumeProvider.BeginRamp(targetVolume, duration);

    public void ApplyLiveSettings(SoundDefinition definition)
    {
        _volumeProvider.Volume = Math.Clamp(definition.Volume, 0.0f, 1.0f);
        _balanceProvider.Pan = definition.Pan;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _decoder.Dispose();
    }

    private static ISampleProvider NormalizeChannels(ISampleProvider provider)
    {
        return provider.WaveFormat.Channels switch
        {
            1 => new MonoToStereoSampleProvider(provider),
            2 => provider,
            _ => throw new NotSupportedException($"{provider.WaveFormat.Channels}チャンネル音源には未対応です。")
        };
    }
}
