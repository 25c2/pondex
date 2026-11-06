using System.Text.Json.Serialization;
using System.Text.Json;

namespace PondashiEX.Project;

public sealed class ProjectModel
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;

    public ProjectMetadata Project { get; set; } = new();

    public ProjectSettings Settings { get; set; } = new();

    public List<SoundDefinition> Sounds { get; set; } = [];

    public List<GroupShortcutDefinition> GroupShortcuts { get; set; } = [];

    public List<TriggerDefinition> Triggers { get; set; } = [];

    [JsonIgnore]
    public List<string> LoadWarnings { get; } = [];

    [JsonIgnore]
    public List<string> MissingFiles { get; } = [];
}

public sealed class ProjectMetadata
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public string Name { get; set; } = "新しいプロジェクト";

    public string ThemeColor { get; set; } = "#202020";
}

public sealed class ProjectSettings
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public string OutputDevice { get; set; } = "default";

    public float MasterVolume { get; set; } = 1.0f;

    public bool MasterMute { get; set; }

    public bool SilentLoop { get; set; }

    public bool FadeOutOnStop { get; set; } = true;

    public bool FadeOutOnStopAll { get; set; }

    public bool DarkMode { get; set; }

    public bool BackgroundInput { get; set; }

    public int SoundListColumns { get; set; } = 1;
}

public sealed class SoundDefinition
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "新しい音源";

    public string File { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    public string Group { get; set; } = string.Empty;

    public float Volume { get; set; } = 1.0f;

    public float Pan { get; set; }

    public float Pitch { get; set; } = 1.0f;

    public float Speed { get; set; } = 1.0f;

    public LoopSettings Loop { get; set; } = new();

    public FadeSettings FadeIn { get; set; } = new();

    public FadeSettings FadeOut { get; set; } = new();

    public string Memo { get; set; } = string.Empty;
}

public sealed class GroupShortcutDefinition
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public string Group { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;
}

public sealed class LoopSettings
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public bool Enabled { get; set; }

    public long StartSample { get; set; }

    public double StartSeconds { get; set; }
}

public sealed class FadeSettings
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public bool Enabled { get; set; }

    public float Value { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<TriggerType>))]
public enum TriggerType
{
    ClockTime,
    StopwatchElapsed,
    SoundEnded,
    SoundElapsed
}

[JsonConverter(typeof(JsonStringEnumConverter<TriggerAction>))]
public enum TriggerAction
{
    Play,
    Stop,
    Pause
}

public sealed class TriggerDefinition
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public Guid Id { get; set; } = Guid.NewGuid();

    public bool Enabled { get; set; } = true;

    public string Memo { get; set; } = string.Empty;

    public TriggerType TriggerType { get; set; }

    public TriggerParameters Parameters { get; set; } = new();

    public TriggerAction Action { get; set; }

    public Guid TargetSoundId { get; set; }

    public string TargetGroup { get; set; } = string.Empty;
}

public sealed class TriggerParameters
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public TimeOnly? ClockTime { get; set; }

    public TimeSpan? Elapsed { get; set; }

    public Guid? SourceSoundId { get; set; }
}
