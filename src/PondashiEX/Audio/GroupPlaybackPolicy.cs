using PondashiEX.Project;

namespace PondashiEX.Audio;

public static class GroupPlaybackPolicy
{
    public static IReadOnlyList<Guid> FindConflicts(
        SoundDefinition requested,
        IEnumerable<SoundDefinition> currentlyPlaying)
    {
        if (string.IsNullOrWhiteSpace(requested.Group))
        {
            return [];
        }

        return currentlyPlaying
            .Where(sound => sound.Id != requested.Id)
            .Where(sound => string.Equals(sound.Group, requested.Group, StringComparison.OrdinalIgnoreCase))
            .Select(sound => sound.Id)
            .ToArray();
    }
}

