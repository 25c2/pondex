using System.Runtime.ExceptionServices;
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
