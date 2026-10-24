using System.Drawing;
using PondashiEX.Audio;
using PondashiEX.Project;
using PondashiEX.UI;

namespace PondashiEX.Tests;

[Collection(WinFormsTestGroup.Name)]
public sealed class UiAuditRegressionTests
{
    [Theory]
    [InlineData(10F)]
    [InlineData(15F)]
    [InlineData(20F)]
    public void PauseCaption_UsesMeasuredFontWidth(float fontSize) => WinFormsTest.Run(() =>
    {
        using var form = new Form { ClientSize = new Size(1000, 500), ShowInTaskbar = false, Opacity = 0 };
        using var card = new SoundCard(new SoundDefinition()) { Width = 700 };
        form.Controls.Add(card);
        form.Show();
        var pause = Descendants(card).OfType<Button>().Single(button => button.Text.StartsWith('Ⅱ'));
        using var font = UiTheme.CreateFont(fontSize, FontStyle.Bold);
        pause.Font = font;
        card.UpdateState(PondashiEX.Audio.PlaybackState.Stopped);
        var measured = TextRenderer.MeasureText(pause.Text, pause.Font).Width;
        Assert.True(measured <= pause.ClientSize.Width, $"{fontSize}pt: {pause.Text} が折り返されます。");
    });

    [Theory]
    [InlineData(356)]
    [InlineData(480)]
    [InlineData(1000)]
    public void ExpandedCard_AllButtonsAreInsideVisibleCard(int width) => WinFormsTest.Run(() =>
    {
        using var form = new Form { ClientSize = new Size(1200, 900), ShowInTaskbar = false, Opacity = 0, Font = UiTheme.CreateFont(10F) };
        using var card = new SoundCard(new SoundDefinition()) { Width = width };
        form.Controls.Add(card);
        form.Show();
        Descendants(card).OfType<Button>().Single(button => button.Text.StartsWith("詳細", StringComparison.Ordinal)).PerformClick();
        Application.DoEvents();
        foreach (var button in Descendants(card).OfType<Button>())
        {
            var bounds = card.RectangleToClient(button.RectangleToScreen(button.ClientRectangle));
            Assert.True(card.ClientRectangle.Contains(bounds), $"幅{width}: {button.Text}: {bounds}, card={card.ClientRectangle}");
            for (var parent = button.Parent; parent is not null && parent != card; parent = parent.Parent)
            {
                var localBounds = parent.RectangleToClient(button.RectangleToScreen(button.ClientRectangle));
                Assert.True(parent.ClientRectangle.Contains(localBounds), $"{button.Text} が {parent.GetType().Name} で隠れています: {localBounds}, parent={parent.ClientRectangle}");
            }
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MinimumDialog_KeepsOkCancelVisibleOutsideScrollingContent(bool trigger) => WinFormsTest.Run(() =>
    {
        using Form dialog = trigger
            ? new TriggerDialog(new TriggerDefinition(), [new SoundDefinition()])
            : new SettingsDialog(new ProjectSettings(), [AudioDeviceInfo.Default]);
        dialog.ShowInTaskbar = false;
        dialog.Opacity = 0;
        dialog.Size = dialog.MinimumSize;
        dialog.Show();
        Application.DoEvents();
        foreach (var button in Descendants(dialog).OfType<Button>())
        {
            var bounds = dialog.RectangleToClient(button.RectangleToScreen(button.ClientRectangle));
            Assert.True(dialog.ClientRectangle.Contains(bounds), $"{button.Text}: {bounds}");
            Assert.True(button.Parent!.Height >= button.Bottom);
        }
        var scroll = dialog.Controls.OfType<Panel>().Single(panel => panel.AutoScroll);
        Assert.True(scroll.VerticalScroll.Visible);
        if (trigger)
        {
            var displays = Descendants(dialog).OfType<ComboBox>().Select(combo => combo.GetItemText(combo.SelectedItem)).ToArray();
            Assert.Contains("指定時刻", displays);
            Assert.Contains("再生", displays);
        }
    });

    [Fact]
    public void Rename_RefreshesTriggerNamesWithoutRebindingOrLosingSelection() => WinFormsTest.Run(() =>
    {
        var sound = new SoundDefinition { Name = "旧名" };
        var trigger = new TriggerDefinition
        {
            TargetSoundId = sound.Id,
            TriggerType = TriggerType.SoundEnded,
            Parameters = new TriggerParameters { SourceSoundId = sound.Id }
        };
        using var panel = new TriggerPanel();
        panel.SetData([trigger], [sound]);
        var grid = Descendants(panel).OfType<DataGridView>().Single();
        var row = grid.Rows[0];
        row.Selected = true;
        sound.Name = "新名";
        panel.RefreshSoundNames();
        Assert.Same(row, grid.Rows[0]);
        Assert.True(row.Selected);
        Assert.Contains("新名", (string)row.Cells[2].Value!);
        Assert.Contains("新名", (string)row.Cells[4].Value!);
    });

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) { yield return descendant; }
        }
    }
}
