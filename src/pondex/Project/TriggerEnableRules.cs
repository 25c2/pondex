namespace pondex.Project;

internal sealed class TriggerEnableRules(IReadOnlyList<SoundDefinition> sounds)
{
    internal const int MaxElapsedSeconds = 7 * 24 * 60 * 60;
    private static readonly TimeSpan MaxElapsed = TimeSpan.FromSeconds(MaxElapsedSeconds);
    private readonly HashSet<Guid> _soundIds = sounds.Select(sound => sound.Id).ToHashSet();
    private readonly HashSet<string> _groups = sounds.Select(sound => sound.Group)
        .Where(group => !string.IsNullOrWhiteSpace(group)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    internal string? GetProblem(TriggerDefinition trigger)
    {
        if (trigger.TargetGroupOnHold) { return "保留中の対象"; }
        if (!Enum.IsDefined(trigger.Action) || !Enum.IsDefined(trigger.TriggerType)) { return "不明な条件・操作"; }
        if (string.IsNullOrWhiteSpace(trigger.TargetGroup)
                ? trigger.TargetSoundId == Guid.Empty || !_soundIds.Contains(trigger.TargetSoundId)
                : !_groups.Contains(trigger.TargetGroup))
        {
            return "対象音源・グループの欠落";
        }
        if (trigger.TriggerType is TriggerType.SoundEnded or TriggerType.SoundElapsed
            && (trigger.Parameters.SourceSoundId is not { } source || source == Guid.Empty || !_soundIds.Contains(source)))
        {
            return "条件音源の欠落";
        }
        if ((trigger.TriggerType == TriggerType.ClockTime && trigger.Parameters.ClockTime is null)
            || (trigger.TriggerType is TriggerType.StopwatchElapsed or TriggerType.SoundElapsed
                && (trigger.Parameters.Elapsed is not { } elapsed || elapsed < TimeSpan.Zero || elapsed > MaxElapsed)))
        {
            return "時刻・経過時間の未指定または範囲外";
        }
        return null;
    }
}
