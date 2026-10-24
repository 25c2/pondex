using PondashiEX.Project;
using PondashiEX.UI;
using System.Reflection;

namespace PondashiEX.Tests;

public sealed class SoundCardTests
{
    [Fact]
    public void Constructor_ShowsPlainNameWithoutDragHandle()
    {
        using var card = new SoundCard(new SoundDefinition { Name = "ベル" });

        Assert.Contains(
            Descendants(card).OfType<Label>(),
            label => label.Text == "ベル" && label.Cursor != Cursors.SizeAll);
    }

    [Fact]
    public void TextEditors_MatchProjectSaveLimits()
    {
        using var card = new SoundCard(new SoundDefinition());
        card.EnsureDetailsCreated();
        var editors = Descendants(card).OfType<TextBox>().ToArray();
        Assert.Contains(editors, editor => editor.Width == 170 && editor.MaxLength == ProjectLimits.MaxNameLength);
        Assert.Contains(editors, editor => editor.Width == 100 && editor.MaxLength == ProjectLimits.MaxGroupLength);
        Assert.Contains(editors, editor => editor.Width == 230 && editor.MaxLength == ProjectLimits.MaxMemoLength);
    }

    [Fact]
    public void ValidatingChangedGroup_RaisesCommittedChange()
    {
        var sound = new SoundDefinition { Group = "SE" };
        using var card = new SoundCard(sound);
        card.EnsureDetailsCreated();
        var groupEditor = Descendants(card)
            .OfType<TextBox>()
            .Single(textBox => textBox.Width == 100);
        GroupChangedEventArgs? changed = null;
        card.GroupChangeCommitted += (_, eventArgs) => changed = eventArgs;
        groupEditor.Text = "SFX";

        RaiseEvent(groupEditor, "OnValidated", EventArgs.Empty);

        Assert.NotNull(changed);
        Assert.Equal("SE", changed.PreviousGroup);
        Assert.Equal("SFX", changed.CurrentGroup);
    }

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
        Assert.True(card.Height < 340, $"展開後の高さが大きすぎます: {card.Height}px");

        detailsButton.PerformClick();
        Assert.Equal(collapsedHeight, card.Height);
    }

    [Fact]
    public void Enter_FinishesTextEditingAndKeepsEnteredValue()
    {
        using var form = new Form();
        using var card = new SoundCard(new SoundDefinition());
        card.EnsureDetailsCreated();
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
        card.EnsureDetailsCreated();
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
        card.EnsureDetailsCreated();
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

    [Fact]
    public void EnablingFadeWithZeroSeconds_SetsUsableOneSecondDefault()
    {
        var sound = new SoundDefinition();
        using var card = new SoundCard(sound);
        card.EnsureDetailsCreated();
        var fadeIn = Descendants(card)
            .OfType<CheckBox>()
            .Single(checkBox => checkBox.Text == "Fade In");

        fadeIn.Checked = true;

        Assert.True(sound.FadeIn.Enabled);
        Assert.Equal(1.0f, sound.FadeIn.Value);
    }

    [Fact]
    public void ExistingEnabledFadeWithZeroSeconds_IsNormalizedToOneSecond()
    {
        var sound = new SoundDefinition
        {
            FadeOut = new FadeSettings { Enabled = true, Value = 0 }
        };

        using var card = new SoundCard(sound);

        Assert.Equal(1.0f, sound.FadeOut.Value);
    }

    [Fact]
    public void VolumeSliderChange_UpdatesNumericPercentage()
    {
        using var card = new SoundCard(new SoundDefinition());
        var volume = Descendants(card).OfType<TrackBar>().Single();
        var numeric = Descendants(card)
            .OfType<NumericUpDown>()
            .Single(control => control.Minimum == 0 && control.Maximum == 100 && control.DecimalPlaces == 0);

        volume.Value = 42;

        Assert.Equal(42, numeric.Value);
    }

    [Fact]
    public void NumericVolumeChange_UpdatesSliderAndSoundDefinition()
    {
        var sound = new SoundDefinition();
        using var card = new SoundCard(sound);
        var volume = Descendants(card).OfType<TrackBar>().Single();
        var numeric = Descendants(card)
            .OfType<NumericUpDown>()
            .Single(control => control.Minimum == 0 && control.Maximum == 100 && control.DecimalPlaces == 0);

        numeric.Value = 37;

        Assert.Equal(37, volume.Value);
        Assert.Equal(0.37f, sound.Volume);
    }

    [Fact]
    public void KeyEditor_RemovesWhitespaceThatCannotBeUsedAsShortcut()
    {
        var sound = new SoundDefinition();
        using var card = new SoundCard(sound);
        card.EnsureDetailsCreated();
        var keyEditor = Descendants(card)
            .OfType<TextBox>()
            .Single(textBox => textBox.MaxLength == 20);

        keyEditor.Text = " A 2 ";

        Assert.Equal("A2", keyEditor.Text);
        Assert.Equal("A2", sound.Key);
    }

    [Fact]
    public void InvalidKey_WhenEditingEndsClearsAssignmentAndRaisesNotice()
    {
        var sound = new SoundDefinition();
        using var card = new SoundCard(sound);
        card.EnsureDetailsCreated();
        var keyEditor = Descendants(card)
            .OfType<TextBox>()
            .Single(textBox => textBox.MaxLength == 20);
        var rejected = false;
        card.InvalidKeyRejected += (_, _) => rejected = true;
        keyEditor.Text = "い";

        RaiseEvent(keyEditor, "OnValidated", EventArgs.Empty);

        Assert.True(rejected);
        Assert.Empty(keyEditor.Text);
        Assert.Empty(sound.Key);
    }

    [Fact]
    public void LoopStartEditor_StoresSecondsAndClearsLegacySample()
    {
        var sound = new SoundDefinition
        {
            Loop = new LoopSettings { Enabled = true, StartSample = 44_100 }
        };
        using var card = new SoundCard(sound);
        card.EnsureDetailsCreated();
        var loopStart = Descendants(card)
            .OfType<NumericUpDown>()
            .Single(numeric => numeric.DecimalPlaces == 2 && numeric.Maximum == 24 * 60 * 60);

        loopStart.Value = 2.5m;

        Assert.Equal(2.5, sound.Loop.StartSeconds);
        Assert.Equal(0, sound.Loop.StartSample);
    }

    [Fact]
    public void PlayButton_CommitsTypedLoopSecondsBeforeRequestingPlayback()
    {
        var sound = new SoundDefinition { Loop = new LoopSettings { Enabled = true } };
        using var card = new SoundCard(sound);
        card.EnsureDetailsCreated();
        var loopStart = Descendants(card)
            .OfType<NumericUpDown>()
            .Single(numeric => numeric.DecimalPlaces == 2 && numeric.Maximum == 24 * 60 * 60);
        var playButton = Descendants(card)
            .OfType<Button>()
            .Single(button => button.Text.Contains("再生", StringComparison.Ordinal));
        loopStart.Text = "2.50";
        double? secondsWhenRequested = null;
        card.PlayRequested += (_, _) => secondsWhenRequested = sound.Loop.StartSeconds;

        playButton.PerformClick();

        Assert.Equal(2.5, secondsWhenRequested);
    }

    private static Button DetailsButton(SoundCard card) => Descendants(card)
        .OfType<Button>()
        .Single(button => button.Text.StartsWith("詳細", StringComparison.Ordinal));

    [Fact]
    public void Details_AreCreatedOnceAndDisposedWithCard()
    {
        var card = new SoundCard(new SoundDefinition());
        Assert.False(card.DetailsCreated);
        Assert.DoesNotContain(Descendants(card).OfType<TextBox>(), editor => editor.Parent is not NumericUpDown);
        card.EnsureDetailsCreated();
        var editors = Descendants(card).OfType<TextBox>().ToArray();
        Assert.NotEmpty(editors);
        card.EnsureDetailsCreated();
        Assert.Equal(editors, Descendants(card).OfType<TextBox>());
        card.Dispose();
        Assert.All(editors, editor => Assert.True(editor.IsDisposed));
    }

    [Fact]
    public void CollapsedVolumeFadeAndPlayDoNotCreateDetailsOrRewriteLoopSettings()
    {
        var sound = new SoundDefinition
        {
            Loop = new LoopSettings { StartSample = 48000 },
            FadeOut = new FadeSettings { Enabled = true, Value = 2 }
        };
        using var card = new SoundCard(sound);
        VolumeFadeRequestedEventArgs? fade = null;
        card.VolumeFadeRequested += (_, args) => fade = args;
        var volume = Descendants(card).OfType<TrackBar>().Single();
        volume.Width = 200;
        RaiseEvent(volume, "OnMouseUp", new MouseEventArgs(MouseButtons.Right, 1, 100, 0, 0));
        Assert.NotNull(fade);
        Assert.Equal(TimeSpan.FromSeconds(2), fade.Duration);
        Descendants(card).OfType<Button>().Single(button => button.Text == "▶ 再生").PerformClick();
        Assert.False(card.DetailsCreated);
        Assert.Equal(48000, sound.Loop.StartSample);
        Assert.Equal(0, sound.Loop.StartSeconds);
    }

    [Fact]
    public void MissingLegacyFile_ShowsNoticeAndPreservesSamplePositionWhenOtherFieldsChange()
    {
        using var directory = new TestDirectory();
        var sound = new SoundDefinition { File = directory.GetPath("missing.wav"), Loop = new LoopSettings { StartSample = 48000 } };
        using var card = new SoundCard(sound);
        card.EnsureDetailsCreated();
        Assert.Contains(Descendants(card).OfType<Label>(), label => label.Text.Contains("秒換算不可", StringComparison.Ordinal));
        Descendants(card).OfType<TrackBar>().Single().Value = 37;
        Descendants(card).OfType<Button>().Single(button => button.Text == "▶ 再生").PerformClick();
        Assert.Equal(48000, sound.Loop.StartSample);
        Assert.Equal(0, sound.Loop.StartSeconds);
    }

    [Theory]
    [InlineData(44100)]
    [InlineData(48000)]
    public void UneditedLegacySamplePosition_IsNotRoundedWhenPlaying(int sampleRate)
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("fractional.wav");
        using (var writer = new NAudio.Wave.WaveFileWriter(path, new NAudio.Wave.WaveFormat(sampleRate, 16, 1)))
        {
            writer.Write(new byte[sampleRate * 2], 0, sampleRate * 2);
        }
        var sound = new SoundDefinition { File = path, Loop = new LoopSettings { StartSample = 12345 } };
        using var card = new SoundCard(sound);
        card.EnsureDetailsCreated();
        var changes = 0;
        card.DefinitionChanged += (_, _) => changes++;
        Descendants(card).OfType<Button>().Single(button => button.Text == "▶ 再生").PerformClick();
        Assert.Equal(12345, sound.Loop.StartSample);
        Assert.Equal(0, sound.Loop.StartSeconds);
        Assert.Equal(0, changes);
    }

    [Theory]
    [InlineData(44100)]
    [InlineData(48000)]
    public void LegacyLoopDisplay_UsesSourceRateAndDoesNotRewriteUneditedSamples(int sampleRate)
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("rate.wav");
        using (var writer = new NAudio.Wave.WaveFileWriter(path, new NAudio.Wave.WaveFormat(sampleRate, 16, 1)))
        {
            writer.Write(new byte[200], 0, 200);
        }
        var sound = new SoundDefinition { File = path, Loop = new LoopSettings { StartSample = sampleRate * 2 } };
        using var card = new SoundCard(sound);
        card.EnsureDetailsCreated();
        var loopStart = Descendants(card).OfType<NumericUpDown>().Single(numeric => numeric.Maximum == 86400);
        Assert.Equal(2, loopStart.Value);
        Descendants(card).OfType<Button>().Single(button => button.Text == "▶ 再生").PerformClick();
        Assert.Equal(sampleRate * 2, sound.Loop.StartSample);
        Assert.Equal(0, sound.Loop.StartSeconds);
    }

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
        RaiseMouseEvent(control, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 1, 1, 0));
        Application.DoEvents();
    }

    private static void RaiseMouseEvent(Control control, string methodName, MouseEventArgs eventArgs)
    {
        RaiseEvent(control, methodName, eventArgs);
        Application.DoEvents();
    }

    private static void RaiseEvent(Control control, string methodName, EventArgs eventArgs)
    {
        var method = typeof(Control).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Control.{methodName}が見つかりません。");
        method.Invoke(control, [eventArgs]);
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
