using System.Runtime.InteropServices;
using pondex.Input;
using pondex.Project;

namespace pondex.Tests;

[Collection(WinFormsTestGroup.Name)]
public sealed class ShortcutMessageFilterTests
{
    [Theory]
    [InlineData(Keys.Space)]
    [InlineData(Keys.Enter)]
    public void PostedKey_ActivatesEitherShortcutOrFocusedButton(Keys key) => WinFormsTest.Run(() =>
    {
        using var form = new Form { ShowInTaskbar = false, Opacity = 0 };
        using var button = new Button { Text = "再生" };
        using var editor = new TextBox { Top = 40 };
        form.Controls.AddRange([button, editor]);
        form.AcceptButton = button;
        var sound = new SoundDefinition { Key = new KeysConverter().ConvertToInvariantString(key)! };
        var shortcuts = 0;
        var clicks = 0;
        button.Click += (_, _) => clicks++;
        var dispatcher = new ShortcutDispatcher(new ShortcutManager(), () => [sound], () => editor.Focused, commands =>
        {
            Assert.Equal(sound.Id, Assert.Single(commands).SoundId);
            shortcuts++;
        });
        var filter = new ShortcutMessageFilter(dispatcher.OnKeyChanged,
            hwnd => Control.FromChildHandle(hwnd)?.FindForm() == form);
        Application.AddMessageFilter(filter);
        try
        {
            form.Show();
            button.Select();
            Application.DoEvents();
            Assert.True(button.Focused);
            WinFormsTest.WithNeutralKeyboard(() =>
            {
                PostKey(button, key, repeat: true);
                Assert.Equal(1, shortcuts);
                Assert.Equal(0, clicks);

                sound.Key = string.Empty;
                dispatcher.RefreshBindings();
                PostKey(button, key);
                Assert.Equal(1, shortcuts);
                Assert.Equal(1, clicks);

                sound.Key = "Space";
                dispatcher.RefreshBindings();
                editor.Select();
                Assert.True(editor.Focused);
                PostKey(editor, Keys.Space);
                Assert.Equal(" ", editor.Text);
                Assert.Equal(1, shortcuts);
            });
        }
        finally { Application.RemoveMessageFilter(filter); }
    });

    private static void PostKey(Control target, Keys key, bool repeat = false)
    {
        Assert.True(PostMessage(target.Handle, 0x0100, (IntPtr)key, (IntPtr)1));
        if (repeat) { Assert.True(PostMessage(target.Handle, 0x0100, (IntPtr)key, (IntPtr)0x40000001)); }
        Assert.True(PostMessage(target.Handle, 0x0101, (IntPtr)key, (IntPtr)unchecked((int)0xc0000001)));
        Application.DoEvents();
    }

    [DllImport("user32.dll", EntryPoint = "PostMessageW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
