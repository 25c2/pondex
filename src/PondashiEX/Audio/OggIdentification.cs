namespace PondashiEX.Audio;

internal static class OggIdentification
{
    // Only the first identification packet is needed. Never search comments or audio
    // payloads for signatures, and bound work on an untrusted container header.
    internal static byte[] ReadPacket(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read | FileShare.Delete);
        Span<byte> header = stackalloc byte[27];
        stream.ReadExactly(header);
        if (!header[..4].SequenceEqual("OggS"u8) || header[4] != 0 || (header[5] & 3) != 2)
        {
            throw new InvalidDataException("Ogg識別ページが不正です。");
        }

        var segments = new byte[header[26]];
        stream.ReadExactly(segments);
        var size = 0;
        foreach (var segment in segments)
        {
            size += segment;
            if (size > 4096)
            {
                break;
            }

            if (segment < 255)
            {
                var packet = new byte[size];
                stream.ReadExactly(packet);
                return packet;
            }
        }

        throw new InvalidDataException("Ogg識別パケットが長すぎるか、途中で切れています。");
    }
}
