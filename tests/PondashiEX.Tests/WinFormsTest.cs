using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using PondashiEX.UI;

namespace PondashiEX.Tests;

// Forms share application color mode and desktop focus, even when created on separate threads.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WinFormsTestGroup
{
    public const string Name = "WinForms UI";
}

internal static class WinFormsTest
{
    internal static void WithNeutralKeyboard(Action action)
    {
        // Synthetic WM_KEY*/WM_MOUSEWHEEL messages otherwise inherit physical
        // Shift/Alt/mouse-button state from the interactive desktop. Only neutralize
        // this test thread's keyboard-state table; do not inject OS key-up events.
        var original = new byte[256];
        Assert.True(GetKeyboardState(original));
        Assert.True(SetKeyboardState(new byte[256]));
        try { action(); }
        finally { Assert.True(SetKeyboardState(original)); }
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKeyboardState([Out] byte[] keyState);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetKeyboardState(byte[] keyState);

    public static void Run(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                UiTheme.ConfigureApplicationColors(false);
                action();
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                UiTheme.ConfigureApplicationColors(false);
            }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "UIテストがタイムアウトしました。");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
