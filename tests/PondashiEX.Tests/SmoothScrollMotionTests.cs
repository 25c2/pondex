using PondashiEX.UI;

namespace PondashiEX.Tests;

public sealed class SmoothScrollMotionTests
{
    [Fact]
    public void WheelOffset_ProgressesGraduallyAndStopsAtDestination()
    {
        var motion = new SmoothScrollMotion();
        motion.AddOffset(120, 0, 1000);
        Assert.Equal(0, motion.Position);
        var previous = 0;
        for (var frame = 0; frame < 40; frame++)
        {
            var position = motion.Advance(TimeSpan.FromMilliseconds(16), 1000);
            Assert.InRange(position, previous, 120);
            if (frame == 0) { Assert.InRange(position, 1, 119); }
            previous = position;
        }

        Assert.Equal(120, motion.Position);
        Assert.False(motion.IsAnimating);
    }

    [Fact]
    public void RepeatedWheelInputAccumulatesAndDirectionReversalIsImmediate()
    {
        var motion = new SmoothScrollMotion();
        motion.AddOffset(120, 0, 1000);
        var first = motion.Advance(TimeSpan.FromMilliseconds(16), 1000);
        motion.AddOffset(120, first, 1000);
        motion.Advance(TimeSpan.FromSeconds(1), 1000);
        Assert.Equal(240, motion.Position);

        motion.AddOffset(120, 240, 1000);
        var beforeReverse = motion.Advance(TimeSpan.FromMilliseconds(16), 1000);
        motion.AddOffset(-60, beforeReverse, 1000);
        Assert.True(motion.Advance(TimeSpan.FromMilliseconds(16), 1000) < beforeReverse);
    }

    [Fact]
    public void BoundsAndContentShrinkNeverOverscroll()
    {
        var motion = new SmoothScrollMotion();
        motion.AddOffset(-120, 0, 100);
        Assert.False(motion.IsAnimating);
        motion.AddOffset(1000, 0, 100);
        motion.Advance(TimeSpan.FromSeconds(1), 100);
        Assert.Equal(100, motion.Position);
        motion.AddOffset(1000, 100, 1000);
        Assert.InRange(motion.Advance(TimeSpan.FromMilliseconds(16), 40), 0, 40);
        Assert.Equal(0, motion.Advance(TimeSpan.FromMilliseconds(16), 0));
        Assert.False(motion.IsAnimating);
    }

    [Fact]
    public void HighResolutionWheelOffsetsKeepFractionalMovement()
    {
        var motion = new SmoothScrollMotion();
        for (var step = 0; step < 8; step++)
        {
            motion.AddOffset(0.25, motion.Position, 100);
            motion.Advance(TimeSpan.FromSeconds(1), 100);
        }

        Assert.Equal(2, motion.Position);
    }

    [Fact]
    public void AnimationDependsOnElapsedTimeRatherThanNumberOfFrames()
    {
        var slow = new SmoothScrollMotion();
        var fast = new SmoothScrollMotion();
        slow.AddOffset(500, 0, 1000);
        fast.AddOffset(500, 0, 1000);
        for (var frame = 0; frame < 10; frame++) { slow.Advance(TimeSpan.FromMilliseconds(16), 1000); }
        for (var frame = 0; frame < 20; frame++) { fast.Advance(TimeSpan.FromMilliseconds(8), 1000); }
        Assert.Equal(slow.Position, fast.Position);
    }

    [Fact]
    public void ExternalPositionChangeCancelsOldDestination()
    {
        var motion = new SmoothScrollMotion();
        motion.AddOffset(500, 0, 1000);
        motion.Advance(TimeSpan.FromMilliseconds(16), 1000);
        motion.AddOffset(60, 700, 1000);
        motion.Advance(TimeSpan.FromSeconds(1), 1000);
        Assert.Equal(760, motion.Position);
    }
}
