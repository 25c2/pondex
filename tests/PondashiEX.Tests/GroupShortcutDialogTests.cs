using PondashiEX.Project;
using PondashiEX.UI;

namespace PondashiEX.Tests;

public sealed class GroupShortcutDialogTests
{
    [Fact]
    public void Constructor_ShowsSavedShortcutAndKeepsOriginalUnchanged()
    {
        var original = new GroupShortcutDefinition { Group = "SE", Key = "G" };
        using var dialog = new GroupShortcutDialog([original], ["SE", "BGM"]);

        var saved = Assert.Single(dialog.Definitions);

        Assert.Equal("SE", saved.Group);
        Assert.Equal("G", saved.Key);
        Assert.NotSame(original, saved);
        Assert.Equal("グループショートカット", dialog.Text);
    }

    [Fact]
    public void AddButton_AddsSelectedGroupAndNormalizedKey()
    {
        using var dialog = new GroupShortcutDialog([], ["SE"]);
        var group = Descendants(dialog).OfType<ComboBox>().Single();
        var key = Descendants(dialog).OfType<TextBox>().Single();
        group.SelectedItem = "SE";
        key.Text = "a";

        var addShortcut = typeof(GroupShortcutDialog).GetMethod(
            "AddShortcut",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("追加処理が見つかりません。");
        addShortcut.Invoke(dialog, null);

        var definition = Assert.Single(dialog.Definitions);
        Assert.Equal("SE", definition.Group);
        Assert.Equal("A", definition.Key);
    }

    [Fact]
    public void MaximumCount_DisablesAndGuardsAdd()
    {
        var definitions = Enumerable.Range(0, ProjectLimits.MaxGroupShortcuts)
            .Select(index => new GroupShortcutDefinition { Group = "SE", Key = $"Key{index}" });
        using var dialog = new GroupShortcutDialog(definitions, ["SE"]);
        Assert.False(Descendants(dialog).OfType<Button>().Single(button => button.Text == "追加").Enabled);
        typeof(GroupShortcutDialog).GetMethod("AddShortcut", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(dialog, null);
        Assert.Equal(ProjectLimits.MaxGroupShortcuts, dialog.Definitions.Count);
    }

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
