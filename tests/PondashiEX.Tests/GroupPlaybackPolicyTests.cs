using PondashiEX.Audio;
using PondashiEX.Project;

namespace PondashiEX.Tests;

public sealed class GroupPlaybackPolicyTests
{
    [Fact]
    public void FindConflicts_ReturnsOnlyOtherSoundsInSameNamedGroup()
    {
        var requested = new SoundDefinition { Id = Guid.NewGuid(), Group = "BGM" };
        var sameGroup = new SoundDefinition { Id = Guid.NewGuid(), Group = "bgm" };
        var otherGroup = new SoundDefinition { Id = Guid.NewGuid(), Group = "SE" };

        var conflicts = GroupPlaybackPolicy.FindConflicts(
            requested,
            [requested, sameGroup, otherGroup]);

        Assert.Equal([sameGroup.Id], conflicts);
    }

    [Fact]
    public void FindConflicts_UngroupedSoundDoesNotStopAnything()
    {
        var requested = new SoundDefinition { Id = Guid.NewGuid(), Group = "" };
        var playing = new SoundDefinition { Id = Guid.NewGuid(), Group = "" };

        Assert.Empty(GroupPlaybackPolicy.FindConflicts(requested, [playing]));
    }
}

