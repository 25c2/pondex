namespace pondex.Project;

public static class ProjectLimits
{
    public const float MaxVolume = 2.0f;
    public const int MaxVolumePercent = 200;
    public const long MaxFileBytes = 8 * 1024 * 1024;
    public const int MaxJsonDepth = 64;
    public const int MaxSounds = 500;
    public const int MaxGroupShortcuts = 500;
    public const int MaxTriggers = 1_000;
    public const int MaxNameLength = 256;
    public const int MaxPathLength = 4_096;
    public const int MaxKeyLength = 32;
    public const int MaxGroupLength = 128;
    public const int MaxMemoLength = 4_096;
    public const int MaxOutputDeviceLength = 1_024;
    public const int MaxThemeColorLength = 32;

    public static float GetMaxVolume(SoundDefinition sound) => sound.AllowVolumeBoost ? MaxVolume : 1.0f;

    public static int GetMaxVolumePercent(SoundDefinition sound) => sound.AllowVolumeBoost ? MaxVolumePercent : 100;
}
