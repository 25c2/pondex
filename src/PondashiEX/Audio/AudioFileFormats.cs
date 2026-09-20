namespace PondashiEX.Audio;

internal static class AudioFileFormats
{
    public const string OpenFileFilter =
        "対応音声 (*.wav;*.mp3;*.ogg;*.opus;*.flac;*.aif;*.aiff;*.m4a;*.aac;*.wma)|*.wav;*.mp3;*.ogg;*.opus;*.flac;*.aif;*.aiff;*.m4a;*.aac;*.wma|すべてのファイル (*.*)|*.*";

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".mp3", ".ogg", ".opus", ".flac", ".aif", ".aiff", ".m4a", ".aac", ".wma"
    };

    public static bool IsSupported(string path) => SupportedExtensions.Contains(Path.GetExtension(path));
}
