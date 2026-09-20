using PondashiEX.Project;
using PondashiEX.UI;
using System.Reflection;

namespace PondashiEX.Tests;

public sealed class SoundCardTests
{
    [Fact]
    public void DetailsExpansion_UsesContentHeightInsteadOfLargeFixedWhitespace()
    {
        using var card = new SoundCard(new SoundDefinition())
        {
            Width = 1_120
        };
        card.CreateControl();
        var detailsButton = Descendants(card)
            .OfType<Button>()
            .Single(button => button.Text.StartsWith("詳細", StringComparison.Ordinal));
        var collapsedHeight = card.Height;

        detailsButton.PerformClick();

        Assert.True(card.Height > collapsedHeight);
        Assert.True(card.Height < 250, $"展開後の高さが大きすぎます: {card.Height}px");

        detailsButton.PerformClick();
        Assert.Equal(collapsedHeight, card.Height);
    }

    [Fact]
    public void Enter_FinishesTextEditingAndKeepsEnteredValue()
    {
        using var form = new Form();
        using var card = new SoundCard(new SoundDefinition());
        form.Controls.Add(card);
        form.Show();
        var detailsButton = DetailsButton(card);
        detailsButton.PerformClick();
        var keyEditor = Descendants(card)
            .OfType<TextBox>()
            .Single(textBox => textBox.MaxLength == 20);
        keyEditor.Focus();
        keyEditor.Text = "A";

        RaiseKeyDown(keyEditor, Keys.Enter);

        Assert.False(keyEditor.ContainsFocus);
        Assert.True(detailsButton.Focused);
        Assert.Equal("A", card.Sound.Key);
    }

    [Fact]
    public void Escape_RestoresValueFromWhenEditingStartedAndFinishesEditing()
    {
        using var form = new Form();
        var sound = new SoundDefinition { Memo = "変更前" };
        using var card = new SoundCard(sound);
        form.Controls.Add(card);
        form.Show();
        var detailsButton = DetailsButton(card);
        detailsButton.PerformClick();
        var memoEditor = Descendants(card)
            .OfType<TextBox>()
            .Single(textBox => textBox.Text == "変更前");
        memoEditor.Focus();
        memoEditor.Text = "変更後";

        RaiseKeyDown(memoEditor, Keys.Escape);

        Assert.False(memoEditor.ContainsFocus);
        Assert.True(detailsButton.Focused);
        Assert.Equal("変更前", memoEditor.Text);
        Assert.Equal("変更前", sound.Memo);
    }

    [Fact]
    public void ClickingDetailBackground_FinishesTextEditing()
    {
        using var form = new Form();
        using var card = new SoundCard(new SoundDefinition());
        form.Controls.Add(card);
        form.Show();
        var detailsButton = DetailsButton(card);
        detailsButton.PerformClick();
        var keyEditor = Descendants(card)
            .OfType<TextBox>()
            .Single(textBox => textBox.MaxLength == 20);
        var memoLabel = Descendants(card)
            .OfType<Label>()
            .Single(label => label.Text == "メモ");
        keyEditor.Focus();

        RaiseMouseDown(memoLabel);

        Assert.False(keyEditor.ContainsFocus);
        Assert.True(detailsButton.Focused);
    }

    private static Button DetailsButton(SoundCard card) => Descendants(card)
        .OfType<Button>()
        .Single(button => button.Text.StartsWith("詳細", StringComparison.Ordinal));

    private static void RaiseKeyDown(Control control, Keys key)
    {
        var method = typeof(Control).GetMethod(
            "OnKeyDown",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Control.OnKeyDownが見つかりません。");
        method.Invoke(control, [new KeyEventArgs(key)]);
        Application.DoEvents();
    }

    private static void RaiseMouseDown(Control control)
    {
        var method = typeof(Control).GetMethod(
            "OnMouseDown",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Control.OnMouseDownが見つかりません。");
        method.Invoke(control, [new MouseEventArgs(MouseButtons.Left, 1, 1, 1, 0)]);
        Application.DoEvents();
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
