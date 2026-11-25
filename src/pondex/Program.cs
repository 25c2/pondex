using System.Security.Cryptography;
using System.Text;
using pondex.Audio;
using pondex.Diagnostics;
using pondex.Project;
using pondex.Trigger;
using pondex.UI;
using pondex.Utility;

namespace pondex;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.SetDefaultFont(UiTheme.CreateFont(10F));
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var smokeTest = args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase);
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userDirectory = Path.Combine(localApplicationData, "pondex");
        var storage = AppStoragePaths.Resolve(AppContext.BaseDirectory, userDirectory, Path.GetTempPath());
        var uiPreferences = new UiPreferences(Path.Combine(userDirectory, "UI"));
        var (logger, logDirectory, localLogFailed) = CreateLogger(storage,
            uiPreferences.UseLocalLogs ? Path.Combine(userDirectory, "Logs") : null);
        logger.Info($"pondexを起動しました。ログ: {logger.CurrentLogPath}");

        Application.ThreadException += (_, eventArgs) =>
        {
            logger.Error("UIスレッドで未処理例外が発生しました。", eventArgs.Exception);
            if (!smokeTest)
            {
                MessageBox.Show(
                    $"予期しないエラーが発生しました。操作を続行できる場合があります。\n\n{eventArgs.Exception.Message}",
                    "pondex",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
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
                using var diagnosticEngine = new AudioEngine(logger);
                var devices = new AudioDeviceManager();
                var binding = AudioOutputBinding.Create(AudioDeviceInfo.Default.PersistentId, devices.CaptureSnapshot())
                    ?? throw new InvalidOperationException("Windows既定の出力先を識別できません。");
                diagnosticEngine.SelectOutputDevice(binding.Device.DeviceNumber);
                using var diagnosticCancellation = new CancellationTokenSource();
                var result = AudioSelfTest.RunAsync(diagnosticEngine, async cancellationToken =>
                {
                    var snapshot = await devices.CaptureSnapshotAsync().WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
                    if (!binding.Matches(snapshot))
                    {
                        diagnosticCancellation.Cancel();
                        diagnosticEngine.EmergencyStopAll();
                        throw new OperationCanceledException("診断中に出力先が変化しました。", cancellationToken);
                    }
                }, logger, diagnosticCancellation.Token).GetAwaiter().GetResult();
                var reportPath = AudioSelfTest.SaveReport(result, logDirectory);
                logger.Info($"動作診断を完了しました: {(result.Success ? "PASS" : "FAIL")} — {reportPath}");
                return result.Success ? 0 : 2;
            }

            var firstInstance = true;
            using var instance = smokeTest ? null : OpenInstanceMarker(out firstInstance);
            if (!firstInstance && MessageBox.Show(
                "同じユーザーで pondex が既に起動しています。音声出力が重なったり、同じプロジェクトの保存が競合したりする場合があります。複数起動しますか？",
                "pondex", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return 0;
            }

            using var audioEngine = smokeTest
                ? new AudioEngine(logger, static (_, _) => new SmokeTestAudioOutput())
                : new AudioEngine(logger);
            using var projectManager = new ProjectManager(new ProjectSerializer());
            var triggerEngine = new TriggerEngine();
            var mainForm = new MainForm(
                projectManager,
                audioEngine,
                new AudioDeviceManager(),
                triggerEngine,
                logger,
                logDirectory,
                new RecentProjectStore(Path.Combine(storage.DataDirectory, "recent-projects.json"),
                    Path.Combine(userDirectory, "recent-projects.json"), AppContext.BaseDirectory,
                    additionalLegacyPaths: [AppStoragePaths.GetLegacyHistoryPath(localApplicationData)]),
                uiPreferences);
            if (logger.CurrentLogPath is null)
            {
                mainForm.Shown += (_, _) => mainForm.ReportLoggingUnavailable();
            }
            if (localLogFailed)
            {
                mainForm.Shown += (_, _) => mainForm.ReportLocalLogFallback();
            }
            if (smokeTest)
            {
                mainForm.Shown += (_, _) => mainForm.BeginInvoke(mainForm.CloseWithoutSavePromptForSmokeTest);
            }

            Application.Run(mainForm);
            if (!uiPreferences.PendingHeaderWrite.Wait(TimeSpan.FromSeconds(1)))
            {
                logger.Warning("終了までにヘッダー設定の書き込みが完了しませんでした。");
            }
            return 0;
        }
        catch (Exception exception)
        {
            logger.Error("アプリケーションを継続できないエラーが発生しました。", exception);
            if (!smokeTest)
            {
                MessageBox.Show(
                    $"pondexを継続できません。\n\n{exception.Message}\n\n詳細は次のログフォルダを確認してください。\n{logDirectory}",
                    "pondex",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            return 1;
        }
        finally
        {
            logger.Info("pondexを終了しました。");
            logger.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static Mutex OpenInstanceMarker(out bool firstInstance)
    {
        var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
        var userHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user)));
        return new Mutex(false, $@"Local\pondex-{userHash}", out firstInstance);
    }

    private static (FileLogger Logger, string Directory, bool LocalLogFailed) CreateLogger(AppStoragePaths storage, string? localDirectory)
    {
        Exception? localFailure = null;
        if (localDirectory is not null)
        {
            try { return (new FileLogger(localDirectory), localDirectory, false); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                localFailure = exception;
            }
        }

        try
        {
            var logger = storage.CanWrite ? new FileLogger(storage.LogDirectory) : new FileLogger(TextWriter.Null, 1);
            if (localFailure is not null) { logger.Error("ローカルログを開始できません。", localFailure); }
            if (!storage.IsPortable)
            {
                logger.Warning($"実行フォルダへ書き込めないため、保存先を切り替えました: {storage.DataDirectory}");
            }
            return (logger, storage.LogDirectory, localFailure is not null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (new FileLogger(TextWriter.Null, 1), storage.LogDirectory, localFailure is not null);
        }
    }

    private sealed class SmokeTestAudioOutput : IAudioOutput
    {
        public event EventHandler<NAudio.Wave.StoppedEventArgs>? PlaybackStopped
        {
            add { }
            remove { }
        }

        public NAudio.Wave.PlaybackState PlaybackState { get; private set; }

        public void Play() => PlaybackState = NAudio.Wave.PlaybackState.Playing;

        public void Stop() => PlaybackState = NAudio.Wave.PlaybackState.Stopped;

        public void Dispose() => PlaybackState = NAudio.Wave.PlaybackState.Stopped;
    }
}
