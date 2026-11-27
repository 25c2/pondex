using System.Text.Json;
using pondex.Project;
using pondex.Utility;

namespace pondex.Audio;

internal readonly record struct OutputDevicePreference(string? SelectionId, bool FileExists)
{
    internal AudioOutputBinding? FindLegacyCandidate(string? legacyId, AudioDeviceSnapshot snapshot)
    {
        if (FileExists || !OutputDevicePreferences.IsValidId(legacyId)
            || string.Equals(legacyId, AudioDeviceInfo.Default.PersistentId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return AudioOutputBinding.Create(legacyId!, snapshot);
    }
}

internal sealed class OutputDevicePreferences(string directory)
{
    private const int MaxBytes = 8 * 1024;
    private const string FileName = "output-device.json";
    private static readonly JsonSerializerOptions ReadOptions = new() { AllowDuplicateProperties = false, MaxDepth = 4 };
    private readonly string _path = Path.Combine(directory, FileName);

    internal OutputDevicePreference Load(IAppLogger logger)
    {
        try
        {
            using var stream = File.OpenRead(_path);
            if (stream.Length > MaxBytes) { throw new InvalidDataException("出力設定ファイルが大きすぎます。"); }
            var bytes = new byte[MaxBytes + 1];
            var count = stream.ReadAtLeast(bytes, bytes.Length, false);
            if (count > MaxBytes) { throw new InvalidDataException("出力設定ファイルが大きすぎます。"); }
            var data = JsonSerializer.Deserialize<StoredOutput>(bytes.AsSpan(0, count), ReadOptions);
            if (!IsValidId(data?.SelectionId)) { throw new InvalidDataException("出力設定の形式が正しくありません。"); }
            return new(data!.SelectionId, true);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return default;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            logger.Warning($"ローカルの出力設定を読み込めませんでした（{exception.GetType().Name}）。");
            return new(null, true);
        }
    }

    internal bool TrySave(string selectionId, IAppLogger logger)
    {
        var temporary = Path.Combine(directory, $".output-{Guid.NewGuid():N}.tmp");
        var ownsTemporary = false;
        try
        {
            if (!IsValidId(selectionId)) { throw new InvalidDataException("出力デバイスIDが正しくありません。"); }
            Directory.CreateDirectory(directory);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsTemporary = true;
                JsonSerializer.Serialize(stream, new StoredOutput(selectionId));
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            logger.Warning($"ローカルの出力設定を保存できませんでした（{exception.GetType().Name}）。");
            return false;
        }
        finally
        {
            if (ownsTemporary)
            {
                try { File.Delete(temporary); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    logger.Warning($"出力設定の一時ファイルを削除できませんでした（{exception.GetType().Name}）。");
                }
            }
        }
    }

    internal static bool IsValidId(string? selectionId) => !string.IsNullOrWhiteSpace(selectionId)
        && selectionId.Length <= ProjectLimits.MaxOutputDeviceLength
        && !selectionId.Any(char.IsControl)
        && !string.Equals(selectionId, ProjectSettings.ImportOutputPlaceholder, StringComparison.OrdinalIgnoreCase);

    private sealed record StoredOutput(string? SelectionId);
}
