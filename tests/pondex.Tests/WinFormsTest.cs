using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using pondex.Audio;
using pondex.UI;
using pondex.Utility;

namespace pondex.Tests;

// Forms share application color mode and desktop focus, even when created on separate threads.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WinFormsTestGroup
{
    public const string Name = "WinForms UI";
}

internal static class WinFormsTest
{
    [ThreadStatic]
    private static Action<string>? _note;

    internal static void Note(string phase) => _note?.Invoke(phase);

    internal static OutputDevicePreferences RememberDefaultOutput(string directory, IAppLogger logger)
    {
        var preferences = new OutputDevicePreferences(Path.Combine(directory, "Audio"));
        Assert.True(preferences.TrySave(AudioDeviceInfo.Default.PersistentId, logger));
        return preferences;
    }

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

    public static void RunAsync(Func<Task> action) => Run(() =>
    {
        using var context = new ApplicationContext();
        using var dispatcher = new Control();
        _ = dispatcher.Handle;
        ExceptionDispatchInfo? failure = null;
        dispatcher.BeginInvoke(async () =>
        {
            try { await action(); }
            catch (Exception exception) { failure = ExceptionDispatchInfo.Capture(exception); }
            finally { context.ExitThread(); }
        });
        Application.Run(context);
        failure?.Throw();
    });

    public static void Run(Action action, TimeSpan? timeout = null)
    {
        Exception? failure = null;
        var phase = "start thread";
        var thread = new Thread(() =>
        {
            try
            {
                _note = value => Volatile.Write(ref phase, value);
                Note("enable visual styles");
                Application.EnableVisualStyles();
                Note("initialize application colors");
                UiTheme.ConfigureApplicationColors(false);
                Note("test action");
                action();
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                Note("restore application colors");
                UiTheme.ConfigureApplicationColors(false);
                _note = null;
            }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(timeout ?? TimeSpan.FromSeconds(30)), $"UIテストがタイムアウトしました: {Volatile.Read(ref phase)}");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
