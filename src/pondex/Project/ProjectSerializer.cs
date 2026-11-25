using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using pondex.Utility;

namespace pondex.Project;

public sealed class ProjectSerializer
{
    private readonly JsonSerializerOptions _options;

    public ProjectSerializer()
    {
        _options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true
        };
        _options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    public async Task<ProjectModel> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            var fullPath = ProjectPath.GetSafeFullPath(path, "プロジェクトパス");
            await using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > ProjectLimits.MaxFileBytes)
            {
                throw new ProjectFormatException(
                    $"プロジェクトファイルが大きすぎます（上限 {ProjectLimits.MaxFileBytes / 1024 / 1024} MiB）。");
            }
            var bytes = new byte[stream.Length];
            await stream.ReadExactlyAsync(bytes, cancellationToken);
            var payloadOffset = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
            using var document = JsonDocument.Parse(
                bytes.AsMemory(payloadOffset),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                    MaxDepth = ProjectLimits.MaxJsonDepth
                });
            var model = DeserializeDocument(document.RootElement);
            model.FileRevision = Convert.ToHexString(SHA256.HashData(bytes));
            ValidateAndNormalize(model);
            foreach (var sound in model.Sounds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string resolved;
                try
                {
                    resolved = ProjectPath.Resolve(fullPath, sound.File);
                    model.ApprovedSoundPaths[sound.Id] = resolved;
                }
                catch (ArgumentException) { resolved = sound.File; }
                if (!File.Exists(resolved))
                {
                    model.MissingFiles.Add($"{sound.Name}: {resolved}");
                    model.MissingSoundIds.Add(sound.Id);
                }
            }
            return model;
        }
        catch (JsonException exception)
        {
            throw new ProjectFormatException(
                $"JSONを読み込めません（行 {exception.LineNumber?.ToString(CultureInfo.InvariantCulture) ?? "不明"}、位置 {exception.BytePositionInLine?.ToString(CultureInfo.InvariantCulture) ?? "不明"}）：{exception.Message}",
                exception);
        }
    }

    public Task<ProjectSaveResult> SaveAsync(string path, ProjectModel model, CancellationToken cancellationToken = default) =>
        SaveAsync(path, model, null, cancellationToken);

    internal async Task<ProjectSaveResult> SaveAsync(string path, ProjectModel model, OperationTiming? timing,
        CancellationToken cancellationToken, ProjectSaveDestination? destination = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(model);
        using (timing?.Measure("normalize")) { ValidateAndNormalize(model); }

        string fullPath, directory;
        using (timing?.Measure("ensure_destination"))
        {
            fullPath = ProjectPath.GetSafeFullPath(path, "保存先パス");
            destination?.VerifyPath(fullPath);
            directory = Path.GetDirectoryName(fullPath)
                ?? throw new ArgumentException("保存先フォルダを特定できません。", nameof(path));
            Directory.CreateDirectory(directory);
        }

        var tempPath = Path.Combine(directory, $".pondex-{Guid.NewGuid():N}.tmp");
        var temporaryBackupPath = tempPath + ".bak";
        var backupPath = fullPath + ".bak";
        var ownsTempFile = false;
        byte[] bytes;
        using (timing?.Measure("serialize")) { bytes = JsonSerializer.SerializeToUtf8Bytes(model, _options); }
        if (timing is not null) { timing.Bytes = bytes.Length; }
        if (bytes.LongLength > ProjectLimits.MaxFileBytes)
        {
            throw new ProjectFormatException(
                $"プロジェクトデータが大きすぎるため保存できません（上限 {ProjectLimits.MaxFileBytes / 1024 / 1024} MiB）。");
        }

        try
        {
            FileStream stream;
            using (timing?.Measure("temp_open"))
            {
                stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    bufferSize: 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
            }
            ownsTempFile = true;
            try
            {
                using (timing?.Measure("temp_write")) { await stream.WriteAsync(bytes, cancellationToken); }
                using (timing?.Measure("flush")) { await stream.FlushAsync(cancellationToken); }
            }
            finally
            {
                using (timing?.Measure("temp_close")) { await stream.DisposeAsync(); }
            }

            using (timing?.Measure("readback"))
            {
                await ValidateTemporaryFileAsync(tempPath, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();

            if (destination is { } approved)
            {
                using var revision = timing?.Measure("revision_check");
                approved.VerifyPath(ProjectPath.GetSafeFullPath(fullPath, "保存先パス"));
                approved.VerifyRevision();
            }

            if (destination is { } confirmed ? confirmed.Revision is not null : File.Exists(fullPath))
            {
                if (timing is not null) { timing.Destination = "replace"; }
                using (timing?.Measure("commit"))
                {
                    File.Replace(tempPath, fullPath, temporaryBackupPath, ignoreMetadataErrors: true);
                }
                // The project is committed. A failed backup rotation must not turn this into a failed save.
                // Rename rather than copy/delete so an existing backup survives a failed rotation.
                try
                {
                    using (timing?.Measure("backup")) { File.Move(temporaryBackupPath, backupPath, overwrite: true); }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    return new ProjectSaveResult(
                        $"本体は保存しましたが、バックアップを整理できませんでした: {exception.Message}",
                        temporaryBackupPath)
                    { FileRevision = Convert.ToHexString(SHA256.HashData(bytes)) };
                }
            }
            else
            {
                if (timing is not null) { timing.Destination = "new"; }
                using (timing?.Measure("commit")) { File.Move(tempPath, fullPath); }
            }

            return new ProjectSaveResult { FileRevision = Convert.ToHexString(SHA256.HashData(bytes)) };
        }
        finally
        {
            using var cleanup = timing?.Measure("temp_cleanup");
            if (ownsTempFile && File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Cleanup must not mask the original write/validation/cancellation failure.
                }
            }
        }
    }

    public ProjectModel CreateSnapshot(ProjectModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(model, _options);
        var snapshot = JsonSerializer.Deserialize<ProjectModel>(bytes, _options)
            ?? throw new ProjectFormatException("プロジェクトのスナップショットを作成できませんでした。");
        ValidateAndNormalize(snapshot);
        return snapshot;
    }

    private async Task ValidateTemporaryFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var model = await JsonSerializer.DeserializeAsync<ProjectModel>(stream, _options, cancellationToken)
            ?? throw new ProjectFormatException("保存用JSONの検証に失敗しました。");
        ValidateAndNormalize(model);
    }

    private ProjectModel DeserializeDocument(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ProjectFormatException("プロジェクトJSONのルートはobjectである必要があります。");
        }

        if (!TryGetProperty(root, "formatVersion", out var versionElement)
            || versionElement.ValueKind != JsonValueKind.Number
            || !versionElement.TryGetInt32(out var formatVersion))
        {
            throw new ProjectFormatException("formatVersionが存在しないか、整数ではありません。");
        }

        var model = new ProjectModel { FormatVersion = formatVersion };
        if (TryGetProperty(root, "project", out var projectElement))
        {
            model.Project = DeserializeOrDefault(
                projectElement,
                new ProjectMetadata(),
                "project",
                model.LoadWarnings);
        }

        if (TryGetProperty(root, "settings", out var settingsElement))
        {
            model.Settings = DeserializeOrDefault(
                settingsElement,
                new ProjectSettings(),
                "settings",
                model.LoadWarnings);
        }

        model.Sounds = DeserializeArray<SoundDefinition>(
            root,
            "sounds",
            "音源",
            model.LoadWarnings,
            ProjectLimits.MaxSounds);
        model.GroupShortcuts = DeserializeArray<GroupShortcutDefinition>(
            root,
            "groupShortcuts",
            "グループショートカット",
            model.LoadWarnings,
            ProjectLimits.MaxGroupShortcuts);
        model.Triggers = DeserializeArray<TriggerDefinition>(
            root,
            "triggers",
            "イベント",
            model.LoadWarnings,
            ProjectLimits.MaxTriggers);
        var known = new HashSet<string>(["formatVersion", "project", "settings", "sounds", "groupShortcuts", "triggers"], StringComparer.OrdinalIgnoreCase);
        model.ExtensionData = root.EnumerateObject().Where(property => !known.Contains(property.Name))
            .ToDictionary(property => property.Name, property => property.Value.Clone());
        return model;
    }

    private List<T> DeserializeArray<T>(
        JsonElement root,
        string propertyName,
        string displayName,
        List<string> warnings,
        int maximumCount)
        where T : class
    {
        var results = new List<T>();
        if (!TryGetProperty(root, propertyName, out var arrayElement))
        {
            return results;
        }

        if (arrayElement.ValueKind != JsonValueKind.Array)
        {
            warnings.Add($"{propertyName}が配列ではないため、空として読み込みました。");
            return results;
        }

        if (arrayElement.GetArrayLength() > maximumCount)
        {
            throw new ProjectFormatException($"{displayName}の件数が上限（{maximumCount}件）を超えています。");
        }

        var index = 0;
        foreach (var element in arrayElement.EnumerateArray())
        {
            try
            {
                var value = element.Deserialize<T>(_options);
                if (value is not null)
                {
                    results.Add(value);
                }
                else
                {
                    warnings.Add($"{displayName}[{index}]がnullのためスキップしました。");
                }
            }
            catch (JsonException exception)
            {
                warnings.Add($"{displayName}[{index}]を読み込めないためスキップしました: {exception.Message}");
            }

            index++;
        }

        return results;
    }

    private T DeserializeOrDefault<T>(
        JsonElement element,
        T defaultValue,
        string displayName,
        List<string> warnings)
        where T : class
    {
        try
        {
            return element.Deserialize<T>(_options) ?? defaultValue;
        }
        catch (JsonException exception)
        {
            warnings.Add($"{displayName}を読み込めないため既定値を使用しました: {exception.Message}");
            return defaultValue;
        }
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static void ValidateAndNormalize(ProjectModel model)
    {
        if (model.FormatVersion != ProjectModel.CurrentFormatVersion)
        {
            throw new ProjectFormatException(
                $"未対応のformatVersionです: {model.FormatVersion}（対応: {ProjectModel.CurrentFormatVersion}）");
        }

        model.Project ??= new ProjectMetadata();
        model.Settings ??= new ProjectSettings();
        model.Project.Name ??= "名称未設定";
        model.Project.ThemeColor ??= "#202020";
        model.Settings.OutputDevice ??= "default";
        ValidateString(model.Project.Name, ProjectLimits.MaxNameLength, "プロジェクト名");
        ValidateString(model.Project.ThemeColor, ProjectLimits.MaxThemeColorLength, "テーマ色");
        ValidateString(model.Settings.OutputDevice, ProjectLimits.MaxOutputDeviceLength, "出力デバイス名");
        model.Settings.MasterVolume = Math.Clamp(model.Settings.MasterVolume, 0.0f, 1.0f);
        model.Settings.SoundListColumns = Math.Clamp(model.Settings.SoundListColumns, 1, 8);

        model.Sounds = (model.Sounds ?? []).OfType<SoundDefinition>().ToList();
        ValidateCount(model.Sounds.Count, ProjectLimits.MaxSounds, "音源");
        var soundIds = new HashSet<Guid>();
        var duplicateSoundIds = new HashSet<Guid>();
        foreach (var sound in model.Sounds)
        {
            if (sound.Id == Guid.Empty || !soundIds.Add(sound.Id))
            {
                if (sound.Id != Guid.Empty && duplicateSoundIds.Add(sound.Id))
                {
                    model.LoadWarnings.Add($"音源ID {sound.Id} が重複しています。該当するイベントの対象を確認してください。");
                }
                sound.Id = Guid.NewGuid();
                soundIds.Add(sound.Id);
            }

            sound.Name ??= "名称未設定";
            sound.File ??= string.Empty;
            sound.Key ??= string.Empty;
            sound.Group ??= string.Empty;
            sound.Memo ??= string.Empty;
            ValidateString(sound.Name, ProjectLimits.MaxNameLength, "音源名");
            ValidateString(sound.File, ProjectLimits.MaxPathLength, "音源パス");
            ValidateString(sound.Key, ProjectLimits.MaxKeyLength, "音源キー");
            ValidateString(sound.Group, ProjectLimits.MaxGroupLength, "音源グループ");
            ValidateString(sound.Memo, ProjectLimits.MaxMemoLength, "音源メモ");
            if (ProjectPath.IsRemoteOrDevice(sound.File))
            {
                throw new ProjectFormatException("音源パスにUNCパスまたはデバイスパスは使用できません。");
            }

            sound.Volume = Math.Clamp(sound.Volume, 0.0f, ProjectLimits.GetMaxVolume(sound));
            sound.Pan = Math.Clamp(sound.Pan, -1.0f, 1.0f);
            sound.Pitch = Math.Clamp(sound.Pitch, 0.5f, 2.0f);
            sound.Speed = Math.Clamp(sound.Speed, 0.5f, 2.0f);
            sound.Loop ??= new LoopSettings();
            sound.Loop.StartSample = Math.Max(0, sound.Loop.StartSample);
            sound.Loop.StartSeconds = Math.Clamp(sound.Loop.StartSeconds, 0.0, 24 * 60 * 60);
            sound.FadeIn ??= new FadeSettings();
            sound.FadeOut ??= new FadeSettings();
            sound.FadeIn.Value = Math.Clamp(sound.FadeIn.Value, 0.0f, 60.0f);
            sound.FadeOut.Value = Math.Clamp(sound.FadeOut.Value, 0.0f, 60.0f);
        }

        model.GroupShortcuts = (model.GroupShortcuts ?? [])
            .OfType<GroupShortcutDefinition>()
            .ToList();
        ValidateCount(model.GroupShortcuts.Count, ProjectLimits.MaxGroupShortcuts, "グループショートカット");
        foreach (var shortcut in model.GroupShortcuts)
        {
            shortcut.Group = shortcut.Group?.Trim() ?? string.Empty;
            shortcut.Key = shortcut.Key?.Trim() ?? string.Empty;
            ValidateString(shortcut.Group, ProjectLimits.MaxGroupLength, "ショートカットのグループ");
            ValidateString(shortcut.Key, ProjectLimits.MaxKeyLength, "ショートカットキー");
        }

        model.Triggers = (model.Triggers ?? []).OfType<TriggerDefinition>().ToList();
        ValidateCount(model.Triggers.Count, ProjectLimits.MaxTriggers, "イベントトリガ");
        var triggerIds = new HashSet<Guid>();
        foreach (var trigger in model.Triggers)
        {
            if (trigger.Id == Guid.Empty || !triggerIds.Add(trigger.Id))
            {
                trigger.Id = Guid.NewGuid();
                triggerIds.Add(trigger.Id);
            }

            trigger.Memo ??= string.Empty;
            trigger.TargetGroup ??= string.Empty;
            trigger.Parameters ??= new TriggerParameters();
            ValidateString(trigger.Memo, ProjectLimits.MaxMemoLength, "イベントメモ");
            ValidateString(trigger.TargetGroup, ProjectLimits.MaxGroupLength, "イベント対象グループ");

            if (!Enum.IsDefined(trigger.Action) || !Enum.IsDefined(trigger.TriggerType))
            {
                trigger.Enabled = false;
                model.LoadWarnings.Add($"イベント「{trigger.Memo}」の操作または条件が不明です。イベントを無効にしました。");
            }

            if (trigger.TargetGroupOnHold) { trigger.Enabled = false; }

            var ambiguousTarget = trigger.TargetGroup.Length == 0 && duplicateSoundIds.Contains(trigger.TargetSoundId);
            var ambiguousSource = trigger.Parameters.SourceSoundId is { } candidate && duplicateSoundIds.Contains(candidate);
            if (ambiguousTarget || ambiguousSource)
            {
                trigger.Enabled = false;
                if (ambiguousTarget) { trigger.TargetSoundId = Guid.Empty; }
                if (ambiguousSource) { trigger.Parameters.SourceSoundId = null; }
                model.LoadWarnings.Add($"イベント「{trigger.Memo}」が重複した音源IDを参照しています。対象を選び直してください。");
            }

            if ((trigger.TargetGroup.Length > 0 && !trigger.TargetGroupOnHold && !model.Sounds.Any(sound =>
                    string.Equals(sound.Group, trigger.TargetGroup, StringComparison.OrdinalIgnoreCase)))
                || (trigger.TargetGroup.Length == 0 && !soundIds.Contains(trigger.TargetSoundId))
                || (trigger.TriggerType is TriggerType.SoundEnded or TriggerType.SoundElapsed
                    && (trigger.Parameters.SourceSoundId is not { } sourceId || !soundIds.Contains(sourceId))))
            {
                model.LoadWarnings.Add($"イベント「{trigger.Memo}」の参照先が見つかりません。対象音源・グループと条件音源を確認してください。");
            }
        }
    }

    private static void ValidateCount(int count, int maximumCount, string displayName)
    {
        if (count > maximumCount)
        {
            throw new ProjectFormatException($"{displayName}の件数が上限（{maximumCount}件）を超えています。");
        }
    }

    private static void ValidateString(string value, int maximumLength, string displayName)
    {
        if (value.Length > maximumLength)
        {
            throw new ProjectFormatException($"{displayName}が長すぎます（上限 {maximumLength}文字）。");
        }
    }
}

public sealed record ProjectSaveResult(string? BackupWarning = null, string? RecoveryBackupPath = null)
{
    internal string? FileRevision { get; init; }
}

public sealed class ProjectFormatException : Exception
{
    public ProjectFormatException(string message)
        : base(message)
    {
    }

    public ProjectFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
