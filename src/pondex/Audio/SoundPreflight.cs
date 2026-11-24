using pondex.Project;

namespace pondex.Audio;

internal static class SoundPreflight
{
    internal static int Check(PlaybackPath[] paths, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        Span<byte> header = stackalloc byte[4096];
        var failed = 0;
        for (var index = 0; index < paths.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var path = paths[index].Resolve();
                cancellationToken.ThrowIfCancellationRequested();
                if (!AudioFileFormats.IsSupported(path))
                {
                    failed++;
                }
                else
                {
                    using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, bufferSize: 1);
                    if (file.Read(header) == 0) { failed++; }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or ArgumentException or NotSupportedException or InvalidOperationException)
            {
                failed++;
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (index % 16 == 0 || index == paths.Length - 1) { progress?.Report(index + 1); }
        }
        return failed;
    }
}
