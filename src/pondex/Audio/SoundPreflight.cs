using pondex.Project;

namespace pondex.Audio;

internal enum SoundCheckMode { Read, Decode }

internal sealed record SoundCheckFailure(string Name, string Reason);

internal sealed record SoundCheckResult(SoundCheckMode Mode, int Total, IReadOnlyList<SoundCheckFailure> Failures)
{
    internal string Summary
    {
        get
        {
            var read = Total - Failures.Count;
            return Mode == SoundCheckMode.Read
                ? $"先頭を読めた音源{read}件。確認できなかった音源{Failures.Count}件。"
                : $"デコードできた音源{read}件。確認できなかった音源{Failures.Count}件。";
        }
    }
}

internal static class SoundPreflight
{
    internal static async Task<SoundCheckResult> CheckAsync((string Name, PlaybackPath Path)[] sources, SoundCheckMode mode,
        IProgress<int>? progress, CancellationToken cancellationToken,
        Func<Action, CancellationToken, Task>? runDecode = null)
    {
        var failures = new List<SoundCheckFailure>();
        for (var index = 0; index < sources.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (name, source) = sources[index];
            void Check() => CheckFile(source, mode, cancellationToken);
            try
            {
                if (mode == SoundCheckMode.Decode && runDecode is not null)
                {
                    await runDecode(Check, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await Task.Run(Check, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
            {
                var reason = exception is FileNotFoundException or DirectoryNotFoundException
                    ? "ファイルが見つかりません。" : exception.Message;
                failures.Add(new SoundCheckFailure(name, reason));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (index % 16 == 0 || index == sources.Length - 1) { progress?.Report(index + 1); }
        }
        return new SoundCheckResult(mode, sources.Length, failures);
    }

    private static void CheckFile(PlaybackPath source, SoundCheckMode mode, CancellationToken cancellationToken)
    {
        var path = source.Resolve();
        cancellationToken.ThrowIfCancellationRequested();
        if (!AudioFileFormats.IsSupported(path)) { throw new NotSupportedException("未対応のファイル形式です。"); }

        if (mode == SoundCheckMode.Read)
        {
            using var stream = ProjectPath.OpenVerifiedRead(path, bufferSize: 1);
            cancellationToken.ThrowIfCancellationRequested();
            Span<byte> header = stackalloc byte[4096];
            if (stream.Read(header) == 0) { throw new InvalidDataException("空のファイルです。"); }
            return;
        }

        using var decoder = AudioDecoderFactory.Open(path, alreadyResolved: true);
        cancellationToken.ThrowIfCancellationRequested();
        var blockAlign = decoder.WaveFormat.BlockAlign;
        if (blockAlign is <= 0 or > 4096) { throw new InvalidDataException("音声ブロックのサイズが不正です。"); }
        var bytes = new byte[4096];
        var count = bytes.Length - bytes.Length % blockAlign;
        if (decoder.Read(bytes, 0, count) == 0) { throw new InvalidDataException("読み取れる音声データがありません。"); }
    }
}
