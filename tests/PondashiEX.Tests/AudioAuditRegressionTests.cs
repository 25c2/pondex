using System.Buffers.Binary;
using System.Reflection;
using NAudio.Wave;
using PondashiEX.Audio;
using PondashiEX.Diagnostics;
using PondashiEX.Project;
using PondashiEX.Utility;

namespace PondashiEX.Tests;

public sealed class AudioAuditRegressionTests
{
    [Theory]
    [InlineData(0xFFFFFF00U, 0xFFFFFF80U, false)]
    [InlineData(0xFFFFFF80U, 0xFFFFFF80U, true)]
    [InlineData(0x00000040U, 0xFFFFFF80U, true)]
    [InlineData(0xFFFFFF80U, 0x00000040U, false)]
    public void OutputPositionComparison_HandlesLongPlaybackCursorWrap(uint played, uint target, bool complete) =>
        Assert.Equal(complete, WaveOutAudioOutput.HasPassedPosition(played, target));

    [Theory]
    [InlineData(20)]
    [InlineData(50)]
    [InlineData(1000)]
    public void Pitch_PreservesDurationAndShortOrTailOnlyAudio(int milliseconds)
    {
        var samples = new float[44100 * milliseconds / 1000];
        var first = Math.Max(0, samples.Length - 882);
        for (var index = first; index < samples.Length; index++)
        {
            samples[index] = 0.5F * MathF.Sin(2 * MathF.PI * 440 * index / 44100);
        }
        var provider = new LivePitchSampleProvider(new Samples(samples)) { Factor = 1.25F };
        var result = ReadAll(provider);
        Assert.Equal(samples.Length, result.Length);
        Assert.True(result.Sum(sample => sample * sample) > 0.1F, "Pitch処理で短音または末尾が消えています。");
    }

    [Fact]
    public void Ramp_ReapplyingSameTargetDoesNotCancelActiveFade()
    {
        var provider = new RampedVolumeSampleProvider(new Samples(Enumerable.Repeat(1F, 44100).ToArray()), 1F);
        provider.BeginRamp(0F, TimeSpan.FromSeconds(1));
        var before = new float[4410];
        provider.Read(before, 0, before.Length);
        provider.Volume = 0F;
        var after = new float[4410];
        provider.Read(after, 0, after.Length);
        Assert.InRange(after[0], 0.89F, 0.91F);
        Assert.InRange(after[^1], 0.79F, 0.81F);
    }

    [Theory]
    [InlineData(0.5F)]
    [InlineData(1.25F)]
    [InlineData(2F)]
    public void Pitch_ChangesToneFrequencyWithoutChangingDuration(float factor)
    {
        var samples = Enumerable.Range(0, 44100)
            .Select(index => 0.25F * MathF.Sin(2 * MathF.PI * 440 * index / 44100)).ToArray();
        var provider = new LivePitchSampleProvider(new Samples(samples)) { Factor = factor };
        var result = ReadAll(provider);
        Assert.Equal(samples.Length, result.Length);
        var crossings = 0;
        for (var index = 4411; index < 39690; index++)
        {
            if (result[index - 1] <= 0 && result[index] > 0) { crossings++; }
        }
        var frequency = crossings / 0.8;
        Assert.InRange(frequency, 440 * factor - 10, 440 * factor + 10);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NaturalEnd_WaitsUntilDeviceConsumesRenderedBlock(bool silentLoop)
    {
        using var directory = new TestDirectory();
        var path = WriteTone(directory.GetPath("short.wav"));
        var output = new BufferedOutput();
        ISampleProvider? provider = null;
        using var engine = new AudioEngine(new NullLogger(), (_, input) => { provider = input; return output; });
        engine.SilentLoopEnabled = silentLoop;
        var sound = new SoundDefinition();
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.PlaybackEnded += (_, args) => { if (args.EndReason == PlaybackEndReason.Natural) { ended.TrySetResult(); } };
        await engine.PlayAsync(sound, path);
        var buffer = new float[4096];
        provider!.Read(buffer, 0, buffer.Length);
        await Task.Delay(50);
        Assert.False(ended.Task.IsCompleted);
        Assert.Equal(PondashiEX.Audio.PlaybackState.Playing, engine.GetState(sound.Id));
        Assert.Contains(buffer, sample => Math.Abs(sample) > 0.01F);
        output.ConsumedSamples = buffer.Length;
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(engine.GetActiveSoundIds());
        Assert.Equal(silentLoop ? NAudio.Wave.PlaybackState.Playing : NAudio.Wave.PlaybackState.Stopped, output.PlaybackState);
    }

    [Fact]
    public async Task PendingNaturalEnd_CannotStopRetriggeredSoundOrUndoEmergencyStop()
    {
        using var directory = new TestDirectory();
        var path = WriteTone(directory.GetPath("short.wav"));
        var output = new BufferedOutput();
        ISampleProvider? provider = null;
        using var engine = new AudioEngine(new NullLogger(), (_, input) => { provider = input; return output; });
        var sound = new SoundDefinition();
        await engine.PlayAsync(sound, path);
        provider!.Read(new float[4096], 0, 4096);
        await engine.PlayAsync(sound, path);
        output.ConsumedSamples = 4096;
        await Task.Delay(50);
        Assert.Equal(PondashiEX.Audio.PlaybackState.Playing, engine.GetState(sound.Id));
        engine.EmergencyStopAll();
        Assert.Empty(engine.GetActiveSoundIds());
        Assert.Equal(NAudio.Wave.PlaybackState.Stopped, output.PlaybackState);
    }

    [Fact]
    public async Task PausedSound_StopWithFadeStopsImmediately()
    {
        using var directory = new TestDirectory();
        var path = WriteTone(directory.GetPath("short.wav"));
        using var engine = new AudioEngine(new NullLogger(), (_, _) => new BufferedOutput());
        var sound = new SoundDefinition { FadeOut = new FadeSettings { Enabled = true, Value = 10 } };
        await engine.PlayAsync(sound, path);
        engine.Pause(sound.Id);
        engine.Stop(sound.Id);
        Assert.Empty(engine.GetActiveSoundIds());
    }

    [Fact]
    public void Opus_NonzeroSeekMatchesSequentialDecodeAndUsesManagedDecoder()
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("tone.opus");
        File.WriteAllBytes(path, Convert.FromBase64String(AudioSelfTestFixtures.OpusBase64));
        using var decoder = new OpusWaveReader(path);
        var implementation = typeof(OpusWaveReader).GetField("_decoder", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(decoder);
        Assert.IsType<Concentus.Structs.OpusDecoder>(implementation);
        var full = ReadBytes(decoder);
        foreach (var frames in new[] { 1, 313, 960, 4800 })
        {
            var target = frames * decoder.WaveFormat.BlockAlign;
            if (target >= full.Length) { continue; }
            decoder.Position = target;
            var sought = ReadBytes(decoder);
            var expected = full.AsSpan(target).ToArray();
            Assert.Equal(expected.Length, sought.Length);
            // An independently warmed Opus decoder need not be bit-identical; the
            // sample offset must match, with only sub-LSB convergence differences.
            double squaredError = 0;
            for (var index = 0; index < sought.Length; index += sizeof(short))
            {
                var difference = BinaryPrimitives.ReadInt16LittleEndian(sought.AsSpan(index))
                    - BinaryPrimitives.ReadInt16LittleEndian(expected.AsSpan(index));
                squaredError += (double)difference * difference;
            }
            Assert.True(Math.Sqrt(squaredError / (sought.Length / 2)) < 5, "Opus seekのサンプル位置がずれています。");
        }
    }

    [Fact]
    public void Opus_HeaderGainIsAppliedAndInvalidLengthDoesNotLeakFile()
    {
        using var directory = new TestDirectory();
        var bytes = Convert.FromBase64String(AudioSelfTestFixtures.OpusBase64);
        var normalPath = directory.GetPath("normal.opus");
        File.WriteAllBytes(normalPath, bytes);
        using var normal = new OpusWaveReader(normalPath);
        var baseline = ReadAll(normal.ToSampleProvider());
        var headerOffset = bytes.AsSpan().IndexOf("OpusHead"u8);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(headerOffset + 16), -12 * 256);
        FixOggChecksums(bytes);
        var gainPath = directory.GetPath("gain.opus");
        File.WriteAllBytes(gainPath, bytes);
        using (var quieter = new OpusWaveReader(gainPath))
        {
            var samples = ReadAll(quieter.ToSampleProvider());
            var ratio = Math.Sqrt(samples.Sum(sample => (double)sample * sample) / baseline.Sum(sample => (double)sample * sample));
            Assert.InRange(ratio, 0.25, 0.253);
        }
        var lastPage = 0;
        for (var offset = 0; offset < bytes.Length; offset += PageLength(bytes, offset)) { lastPage = offset; }
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(lastPage + 6), long.MaxValue);
        FixOggChecksums(bytes);
        var invalidPath = directory.GetPath("overflow.opus");
        File.WriteAllBytes(invalidPath, bytes);
        Assert.Throws<OverflowException>(() => new OpusWaveReader(invalidPath));
        using var exclusive = File.Open(invalidPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public void Opus_SeekAcrossPagesMatchesNonperiodicAudio()
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("noise.opus");
#pragma warning disable CS0618 // Avoid native probing in regression fixture generation too.
        using var encoder = new Concentus.Structs.OpusEncoder(48000, 1, Concentus.Enums.OpusApplication.OPUS_APPLICATION_AUDIO);
#pragma warning restore CS0618
        using (var output = File.Create(path))
        {
            var writer = new Concentus.Oggfile.OpusOggWriteStream(encoder, output, leaveOpen: true)
            { MaxAudioLengthPerPage = TimeSpan.FromMilliseconds(200) };
            var samples = new short[48000 * 2];
            var random = new Random(8675309);
            for (var index = 0; index < samples.Length; index++) { samples[index] = (short)random.Next(-8000, 8000); }
            writer.WriteSamples(samples, 0, samples.Length);
            writer.Finish();
        }
        using var decoder = new OpusWaveReader(path);
        var baseline = ReadBytes(decoder);
        ReadOnlySpan<int> offsets = [415, 510, 1013];
        foreach (var milliseconds in offsets)
        {
            var target = milliseconds * 48 * decoder.WaveFormat.BlockAlign;
            decoder.Position = target;
            var result = ReadBytes(decoder);
            Assert.Equal(baseline.Length - target, result.Length);
            long difference = 0;
            var compared = 0;
            for (var index = 9600; index + 1 < result.Length; index += 2)
            {
                difference += Math.Abs(BinaryPrimitives.ReadInt16LittleEndian(result.AsSpan(index))
                    - BinaryPrimitives.ReadInt16LittleEndian(baseline.AsSpan(target + index)));
                compared++;
            }
            Assert.True(difference / (double)compared < 2, $"ページをまたぐOpus seekが不正: {milliseconds} ms");
        }
    }

    [Fact]
    public void Ogg_OpusSignatureInsideVorbisCommentIsNotMisclassified()
    {
        using var directory = new TestDirectory();
        var bytes = Convert.FromBase64String(AudioSelfTestFixtures.OggBase64);
        var comment = bytes.AsSpan().IndexOf("\x03vorbis"u8);
        Assert.True(comment >= 0);
        var vendorLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(comment + 7));
        Assert.True(vendorLength >= 8);
        "OpusHead"u8.CopyTo(bytes.AsSpan(comment + 11));
        FixOggChecksums(bytes);
        var path = directory.GetPath("vorbis.ogg");
        File.WriteAllBytes(path, bytes);
        using var decoder = AudioDecoderFactory.Open(path);
        Assert.Contains(ReadAll(decoder.ToSampleProvider()), sample => Math.Abs(sample) > 0.01F);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(255)]
    public void Opus_SingleStreamIdentityMappingsRemainSupported(byte family)
    {
        using var directory = new TestDirectory();
        var bytes = Convert.FromBase64String(AudioSelfTestFixtures.OpusBase64);
        var head = bytes.AsSpan().IndexOf("OpusHead"u8);
        var channels = bytes[head + 9];
        var expanded = new byte[bytes.Length + 2 + channels];
        bytes.AsSpan(0, head + 19).CopyTo(expanded);
        expanded[head + 18] = family;
        expanded[head + 19] = 1;
        expanded[head + 20] = (byte)(channels - 1);
        for (var channel = 0; channel < channels; channel++) { expanded[head + 21 + channel] = (byte)channel; }
        bytes.AsSpan(head + 19).CopyTo(expanded.AsSpan(head + 21 + channels));
        expanded[27] += (byte)(2 + channels);
        FixOggChecksums(expanded);
        var path = directory.GetPath("mapped.opus");
        File.WriteAllBytes(path, expanded);
        using var decoder = AudioDecoderFactory.Open(path);
        Assert.Contains(ReadAll(decoder.ToSampleProvider()), sample => Math.Abs(sample) > 0.01F);
    }

    private static int PageLength(byte[] bytes, int offset) => 27 + bytes[offset + 26]
        + bytes.AsSpan(offset + 27, bytes[offset + 26]).ToArray().Sum(value => value);

    private static void FixOggChecksums(byte[] bytes)
    {
        for (var offset = 0; offset < bytes.Length;)
        {
            var length = PageLength(bytes, offset);
            bytes.AsSpan(offset + 22, 4).Clear();
            uint crc = 0;
            foreach (var value in bytes.AsSpan(offset, length))
            {
                crc ^= (uint)value << 24;
                for (var bit = 0; bit < 8; bit++) { crc = (crc << 1) ^ ((crc & 0x80000000) != 0 ? 0x04c11db7U : 0); }
            }
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 22), crc);
            offset += length;
        }
    }

    private static byte[] ReadBytes(WaveStream source)
    {
        using var output = new MemoryStream();
        source.CopyTo(output);
        return output.ToArray();
    }

    private static float[] ReadAll(ISampleProvider source)
    {
        var result = new List<float>();
        var buffer = new float[1024];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0) { result.AddRange(buffer.AsSpan(0, read).ToArray()); }
        return result.ToArray();
    }

    private static string WriteTone(string path)
    {
        using var writer = new WaveFileWriter(path, new WaveFormat(44100, 16, 1));
        for (var index = 0; index < 882; index++) { writer.WriteSample(0.5F * MathF.Sin(2 * MathF.PI * 440 * index / 44100)); }
        return path;
    }

    private sealed class Samples(float[] samples) : ISampleProvider
    {
        private int _position;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);
        public int Read(float[] buffer, int offset, int count)
        {
            var read = Math.Min(count, samples.Length - _position);
            Array.Copy(samples, _position, buffer, offset, read);
            _position += read;
            return read;
        }
    }

    private sealed class BufferedOutput : IAudioOutput
    {
        public event EventHandler<StoppedEventArgs>? PlaybackStopped { add { } remove { } }
        public NAudio.Wave.PlaybackState PlaybackState { get; private set; }
        public long ConsumedSamples;
        public long? PlayedSamples => Interlocked.Read(ref ConsumedSamples);
        public void Play() => PlaybackState = NAudio.Wave.PlaybackState.Playing;
        public void Stop() => PlaybackState = NAudio.Wave.PlaybackState.Stopped;
        public void Dispose() => Stop();
    }

    private sealed class NullLogger : IAppLogger
    {
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }
}
