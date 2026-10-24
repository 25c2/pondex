using System.Reflection;
using PondashiEX.Project;
using PondashiEX.UI;

namespace PondashiEX.Tests;

public sealed class SoundOrderDialogTests
{
    [Fact]
    public void MoveSelectedTo_MovesBothDirectionsAndPreservesSelection()
    {
        var sounds = Enumerable.Range(1, 6).Select(index => new SoundDefinition { Name = $"音源 {index}" }).ToList();
        using var dialog = ShowDialog(sounds);
        var list = Descendants(dialog).OfType<ListView>().Single();

        Assert.True(dialog.MoveSelectedTo(5));
        Assert.Equal(sounds[0].Id, dialog.OrderedSoundIds[5]);
        Assert.Equal(5, Assert.Single(list.SelectedIndices.Cast<int>()));
        Assert.True(dialog.MoveSelectedTo(2));
        Assert.Equal(sounds[0].Id, dialog.OrderedSoundIds[2]);
        Assert.Equal(2, Assert.Single(list.SelectedIndices.Cast<int>()));
        Assert.True(dialog.MoveSelectedTo(0));
        Assert.Equal(sounds.Select(sound => sound.Id), dialog.OrderedSoundIds);
        Assert.False(dialog.MoveSelectedTo(-1));
        Assert.False(dialog.MoveSelectedTo(6));
        Assert.False(dialog.MoveSelectedTo(0));
    }

    [Fact]
    public void MoveByPosition_UsesOneBasedPositionAndCancelKeepsOriginalList()
    {
        var sounds = Enumerable.Range(1, 5).Select(index => new SoundDefinition { Name = $"音源 {index}" }).ToList();
        var originalOrder = sounds.Select(sound => sound.Id).ToArray();
        using var dialog = ShowDialog(sounds);
        var position = Descendants(dialog).OfType<NumericUpDown>().Single();
        position.Value = 3;
        Button(dialog, "番へ移動").PerformClick();

        Assert.Equal(sounds[0].Id, dialog.OrderedSoundIds[2]);
        Assert.Equal(3, position.Value);
        Assert.Equal(originalOrder, sounds.Select(sound => sound.Id));
        Button(dialog, "キャンセル").PerformClick();
        Assert.Equal(DialogResult.Cancel, dialog.DialogResult);
        Assert.Equal(originalOrder, sounds.Select(sound => sound.Id));
    }

    [Fact]
    public void KeyboardMovesSelectionAndBoundaryButtonsAreDisabled()
    {
        var sounds = new[] { new SoundDefinition { Name = "A" }, new SoundDefinition { Name = "B" } };
        using var dialog = ShowDialog(sounds);
        Assert.False(Button(dialog, "↑ 上へ").Enabled);
        Assert.False(Button(dialog, "先頭へ").Enabled);
        var processKey = typeof(SoundOrderDialog).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var args = new object[] { Message.Create(IntPtr.Zero, 0x100, IntPtr.Zero, IntPtr.Zero), Keys.Alt | Keys.Down };

        Assert.True((bool)processKey.Invoke(dialog, args)!);

        Assert.Equal(new[] { sounds[1].Id, sounds[0].Id }, dialog.OrderedSoundIds);
        Assert.False(Button(dialog, "↓ 下へ").Enabled);
        Assert.False(Button(dialog, "末尾へ").Enabled);
        Assert.True(Button(dialog, "↑ 上へ").Enabled);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void EmptyAndSingleSoundLists_HandleBoundaries(int count)
    {
        using var dialog = ShowDialog(Enumerable.Range(0, count).Select(_ => new SoundDefinition()));
        Assert.False(Button(dialog, "↑ 上へ").Enabled);
        Assert.False(Button(dialog, "↓ 下へ").Enabled);
        Assert.False(dialog.MoveSelectedTo(0));
    }

    private static SoundOrderDialog ShowDialog(IEnumerable<SoundDefinition> sounds)
    {
        var dialog = new SoundOrderDialog(sounds) { ShowInTaskbar = false, Opacity = 0 };
        dialog.Show();
        Application.DoEvents();
        return dialog;
    }

    private static Button Button(Control parent, string text) => Descendants(parent).OfType<Button>().Single(button => button.Text == text);

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
