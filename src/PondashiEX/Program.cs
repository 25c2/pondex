using PondashiEX.Audio;
using PondashiEX.Diagnostics;
using PondashiEX.Project;
using PondashiEX.Trigger;
using PondashiEX.UI;
using PondashiEX.Utility;

namespace PondashiEX;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.SetDefaultFont(UiTheme.CreateFont(10F));
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var (logger, logDirectory) = CreateLogger();
        logger.Info("PondashiEXを起動しました。");

        Application.ThreadException += (_, eventArgs) =>
        {
            logger.Error("UIスレッドで未処理例外が発生しました。", eventArgs.Exception);
            MessageBox.Show(
                $"予期しないエラーが発生しました。操作を続行できる場合があります。\n\n{eventArgs.Exception.Message}",
                "PondashiEX",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            logger.Error("重大な未処理例外が発生しました。", eventArgs.ExceptionObject as Exception);
        };

        try
        {
            if (SoakSelfTest.TryParseDuration(args, out var soakDuration))
            {
                var result = SoakSelfTest.RunAsync(soakDuration, logger).GetAwaiter().GetResult();
                var reportPath = SoakSelfTest.SaveReport(result, logDirectory);
                logger.Info($"soak診断を完了しました: {(result.Success ? "PASS" : "FAIL")} — {reportPath}");
                return result.Success ? 0 : 3;
            }

            if (args.Contains("--audio-self-test", StringComparer.OrdinalIgnoreCase))
            {
                var result = AudioSelfTest.RunAsync(logger).GetAwaiter().GetResult();
                var reportPath = AudioSelfTest.SaveReport(result, logDirectory);
                logger.Info($"動作診断を完了しました: {(result.Success ? "PASS" : "FAIL")} — {reportPath}");
                return result.Success ? 0 : 2;
            }

            using var audioEngine = new AudioEngine(logger);
            var projectManager = new ProjectManager(new ProjectSerializer());
            var triggerEngine = new TriggerEngine();
            var mainForm = new MainForm(
                projectManager,
                audioEngine,
                new AudioDeviceManager(),
                triggerEngine,
                logger,
                logDirectory);
            if (args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase))
            {
                mainForm.Shown += (_, _) => mainForm.BeginInvoke(mainForm.CloseWithoutSavePromptForSmokeTest);
            }

            Application.Run(mainForm);
            return 0;
        }
        catch (Exception exception)
        {
            logger.Error("アプリケーションを継続できないエラーが発生しました。", exception);
            MessageBox.Show(
                $"PondashiEXを継続できません。\n\n{exception.Message}\n\n詳細は次のログフォルダを確認してください。\n{logDirectory}",
                "PondashiEX",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
        finally
        {
            logger.Info("PondashiEXを終了しました。");
            logger.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static (FileLogger Logger, string Directory) CreateLogger()
    {
        var preferredDirectory = Path.Combine(AppContext.BaseDirectory, "Logs");
        try
        {
            return (new FileLogger(preferredDirectory), preferredDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Read-only installation folders must not prevent a stage application from starting.
            var fallbackDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PondashiEX",
                "Logs");
            var logger = new FileLogger(fallbackDirectory);
            logger.Warning(
                $"実行フォルダへログを書き込めないため、ユーザーログへ切り替えました: {exception.Message}");
            return (logger, fallbackDirectory);
        }
    }
}
