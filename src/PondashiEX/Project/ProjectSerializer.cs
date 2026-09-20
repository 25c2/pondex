using System.Text;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PondashiEX.Project;

public sealed class ProjectSerializer
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
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
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var document = await JsonDocument.ParseAsync(
                stream,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                },
                cancellationToken);
            var model = DeserializeDocument(document.RootElement);
            ValidateAndNormalize(model);
            return model;
        }
        catch (JsonException exception)
        {
            throw new ProjectFormatException(
                $"JSONを読み込めません（行 {exception.LineNumber?.ToString(CultureInfo.InvariantCulture) ?? "不明"}、位置 {exception.BytePositionInLine?.ToString(CultureInfo.InvariantCulture) ?? "不明"}）：{exception.Message}",
                exception);
        }
    }

    public async Task SaveAsync(string path, ProjectModel model, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(model);
        ValidateAndNormalize(model);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("保存先フォルダを特定できません。", nameof(path));
        Directory.CreateDirectory(directory);

        var tempPath = fullPath + ".tmp";
        var backupPath = fullPath + ".bak";
        var json = JsonSerializer.Serialize(model, _options);
        var bytes = Utf8NoBom.GetBytes(json + Environment.NewLine);

        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            await ValidateTemporaryFileAsync(tempPath, cancellationToken);

            if (File.Exists(fullPath))
            {
                File.Replace(tempPath, fullPath, backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, fullPath);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
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
            model.LoadWarnings);
        model.Triggers = DeserializeArray<TriggerDefinition>(
            root,
            "triggers",
            "イベント",
            model.LoadWarnings);
        return model;
    }

    private List<T> DeserializeArray<T>(
        JsonElement root,
        string propertyName,
        string displayName,
        List<string> warnings)
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
        model.Settings.MasterVolume = Math.Clamp(model.Settings.MasterVolume, 0.0f, 1.0f);
        model.Settings.SoundListColumns = Math.Clamp(model.Settings.SoundListColumns, 1, 2);

        model.Sounds = (model.Sounds ?? []).OfType<SoundDefinition>().ToList();
        var soundIds = new HashSet<Guid>();
        foreach (var sound in model.Sounds)
        {
            if (sound.Id == Guid.Empty || !soundIds.Add(sound.Id))
            {
                sound.Id = Guid.NewGuid();
                soundIds.Add(sound.Id);
            }

            sound.Name ??= "名称未設定";
            sound.File ??= string.Empty;
            sound.Key ??= string.Empty;
            sound.Group ??= string.Empty;
            sound.Memo ??= string.Empty;
            sound.Volume = Math.Clamp(sound.Volume, 0.0f, 1.0f);
            sound.Pan = Math.Clamp(sound.Pan, -1.0f, 1.0f);
            sound.Pitch = Math.Clamp(sound.Pitch, 0.5f, 2.0f);
            sound.Loop ??= new LoopSettings();
            sound.Loop.StartSample = Math.Max(0, sound.Loop.StartSample);
            sound.Loop.StartSeconds = Math.Clamp(sound.Loop.StartSeconds, 0.0, 24 * 60 * 60);
            sound.FadeIn ??= new FadeSettings();
            sound.FadeOut ??= new FadeSettings();
            sound.FadeIn.Value = Math.Clamp(sound.FadeIn.Value, 0.0f, 60.0f);
            sound.FadeOut.Value = Math.Clamp(sound.FadeOut.Value, 0.0f, 60.0f);
        }

        model.Triggers = (model.Triggers ?? []).OfType<TriggerDefinition>().ToList();
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
        }
    }
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
