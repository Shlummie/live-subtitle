using System.Windows;
using System.Windows.Interop;
using LiveSubtitle.App.Engine;
using LiveSubtitle.App.Services;
using LiveSubtitle.App.ViewModels;
using LiveSubtitle.App.Views;

namespace LiveSubtitle.App;

public partial class App : System.Windows.Application
{
    private SanitizedLogger? logger;
    private int handlingUnhandledUiException;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (TryGetEnginePipe(e.Args, out var pipeName))
        {
            var exitCode = await EngineHost.RunAsync(pipeName);
            Shutdown(exitCode);
            return;
        }

        if (TryGetSelfTestArguments(e.Args, out var selfTest))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var exitCode = await EngineSelfTest.RunAsync(
                selfTest.AudioPath,
                selfTest.ResultPath,
                selfTest.ExpectedText,
                selfTest.ModelPath,
                selfTest.Backend);
            Shutdown(exitCode);
            return;
        }

        if (TryGetCaptureSelfTestArguments(e.Args, out var captureSelfTest))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var exitCode = await LiveSubtitle.App.Audio.CaptureSelfTest.RunAsync(
                captureSelfTest.SourceKind,
                captureSelfTest.ProcessPath,
                captureSelfTest.ResultPath,
                captureSelfTest.ReadyPath,
                TimeSpan.FromSeconds(captureSelfTest.DurationSeconds),
                captureSelfTest.ExpectedText,
                captureSelfTest.ForbiddenText);
            Shutdown(exitCode);
            return;
        }

        if (TryGetSimultaneousSourceSelfTestArguments(e.Args, out var simultaneousSelfTest))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var exitCode = await LiveSubtitle.App.Audio.SimultaneousSourceSelfTest.RunAsync(
                simultaneousSelfTest.ProcessA,
                simultaneousSelfTest.ProcessB,
                simultaneousSelfTest.ResultPath,
                simultaneousSelfTest.ReadyPath,
                TimeSpan.FromSeconds(simultaneousSelfTest.DurationSeconds));
            Shutdown(exitCode);
            return;
        }

        if (TryGetSoakSelfTestArguments(e.Args, out var soakSelfTest))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var exitCode = await SoakSelfTest.RunAsync(
                soakSelfTest.AudioPath,
                soakSelfTest.ResultPath,
                soakSelfTest.ReadyPath,
                TimeSpan.FromSeconds(soakSelfTest.DurationSeconds),
                TimeSpan.FromSeconds(soakSelfTest.SilenceSeconds));
            Shutdown(exitCode);
            return;
        }

        var restartSelfTestResult = ReadArgument(e.Args, "--engine-restart-self-test-result");
        if (!string.IsNullOrWhiteSpace(restartSelfTestResult))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var exitCode = await EngineRestartSelfTest.RunAsync(restartSelfTestResult);
            Shutdown(exitCode);
            return;
        }

        var paths = new AppPaths();
        paths.EnsureCreated();
        logger = new SanitizedLogger(paths);
        DispatcherUnhandledException += (_, args) =>
        {
            _ = logger.ErrorAsync("ui-unhandled", args.Exception);
            args.Handled = true;
            if (Interlocked.Exchange(ref handlingUnhandledUiException, 1) != 0) return;

            var detail = args.Exception.Message;
            Dispatcher.BeginInvoke(() =>
            {
                foreach (Window window in Windows.Cast<Window>().ToArray()) window.Hide();
                MessageBox.Show(
                    $"LiveSubtitle hit an unexpected problem.\n\n{detail}\n\nA sanitized diagnostic was written to the local Logs folder.",
                    "LiveSubtitle",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(-1);
            });
        };

        try
        {
            var settingsService = new SettingsService(paths);
            var settings = await settingsService.LoadAsync();
            ThemeManager.Apply(settings.General.Theme);
            var viewModel = new MainViewModel(
                settings,
                paths,
                settingsService,
                new ModelDownloadService(paths),
                new HistoryService(paths),
                logger);
            var overlay = new OverlayWindow(viewModel);
            var window = new MainWindow(viewModel, overlay, logger);
            MainWindow = window;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            window.Show();
            if (e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase)) window.Hide();
            await logger.InfoAsync(
                "ui-window",
                $"visible={window.IsVisible}; loaded={window.IsLoaded}; state={window.WindowState}; handle={new WindowInteropHelper(window).Handle}");
            window.IsVisibleChanged += async (_, _) => await logger.InfoAsync(
                "ui-visibility",
                $"visible={window.IsVisible}; loaded={window.IsLoaded}; state={window.WindowState}; handle={new WindowInteropHelper(window).Handle}");
            await logger.InfoAsync("app-start", $"version={typeof(App).Assembly.GetName().Version}");
        }
        catch (Exception exception)
        {
            await logger.ErrorAsync("startup-failed", exception);
            MessageBox.Show(
                $"LiveSubtitle could not start.\n\n{exception.Message}",
                "LiveSubtitle",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static bool TryGetEnginePipe(string[] args, out string pipeName)
    {
        pipeName = string.Empty;
        if (!args.Contains("--engine", StringComparer.OrdinalIgnoreCase)) return false;
        var index = Array.FindIndex(args, argument => string.Equals(argument, "--pipe", StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length) return false;
        pipeName = args[index + 1];
        return !string.IsNullOrWhiteSpace(pipeName);
    }

    private static bool TryGetSelfTestArguments(string[] args, out SelfTestArguments arguments)
    {
        arguments = default;
        var audioPath = ReadArgument(args, "--self-test-audio");
        var resultPath = ReadArgument(args, "--self-test-result");
        if (string.IsNullOrWhiteSpace(audioPath) || string.IsNullOrWhiteSpace(resultPath)) return false;
        var backend = Enum.TryParse<LiveSubtitle.Core.InferenceBackend>(
            ReadArgument(args, "--self-test-backend"),
            ignoreCase: true,
            out var parsedBackend)
            ? parsedBackend
            : LiveSubtitle.Core.InferenceBackend.Auto;
        arguments = new SelfTestArguments(
            audioPath,
            resultPath,
            ReadArgument(args, "--self-test-expected"),
            ReadArgument(args, "--self-test-model"),
            backend);
        return true;
    }

    private static string? ReadArgument(string[] args, string name)
    {
        var index = Array.FindIndex(args, argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static bool TryGetCaptureSelfTestArguments(string[] args, out CaptureSelfTestArguments arguments)
    {
        arguments = default;
        var resultPath = ReadArgument(args, "--capture-self-test-result");
        var readyPath = ReadArgument(args, "--capture-self-test-ready");
        if (string.IsNullOrWhiteSpace(resultPath) || string.IsNullOrWhiteSpace(readyPath)) return false;
        var sourceKind = Enum.TryParse<LiveSubtitle.Core.AudioSourceKind>(
            ReadArgument(args, "--capture-self-test-kind"),
            ignoreCase: true,
            out var parsedKind)
            ? parsedKind
            : LiveSubtitle.Core.AudioSourceKind.OutputDevice;
        var durationSeconds = int.TryParse(ReadArgument(args, "--capture-self-test-duration"), out var parsedDuration)
            ? Math.Clamp(parsedDuration, 2, 120)
            : 20;
        arguments = new CaptureSelfTestArguments(
            sourceKind,
            ReadArgument(args, "--capture-self-test-process"),
            resultPath,
            readyPath,
            durationSeconds,
            ReadArgument(args, "--capture-self-test-expected"),
            ReadArgument(args, "--capture-self-test-forbidden"));
        return true;
    }

    private static bool TryGetSoakSelfTestArguments(string[] args, out SoakSelfTestArguments arguments)
    {
        arguments = default;
        var audioPath = ReadArgument(args, "--soak-self-test-audio");
        var resultPath = ReadArgument(args, "--soak-self-test-result");
        var readyPath = ReadArgument(args, "--soak-self-test-ready");
        if (string.IsNullOrWhiteSpace(audioPath)
            || string.IsNullOrWhiteSpace(resultPath)
            || string.IsNullOrWhiteSpace(readyPath)) return false;
        var duration = int.TryParse(ReadArgument(args, "--soak-self-test-duration"), out var durationSeconds)
            ? Math.Clamp(durationSeconds, 20, 7_200)
            : 3_600;
        var silence = int.TryParse(ReadArgument(args, "--soak-self-test-silence"), out var silenceSeconds)
            ? Math.Clamp(silenceSeconds, 5, duration - 5)
            : Math.Min(600, duration - 5);
        arguments = new SoakSelfTestArguments(audioPath, resultPath, readyPath, duration, silence);
        return true;
    }

    private static bool TryGetSimultaneousSourceSelfTestArguments(
        string[] args,
        out SimultaneousSourceSelfTestArguments arguments)
    {
        arguments = default;
        var resultPath = ReadArgument(args, "--simultaneous-self-test-result");
        var readyPath = ReadArgument(args, "--simultaneous-self-test-ready");
        var processA = ReadArgument(args, "--simultaneous-process-a");
        var processB = ReadArgument(args, "--simultaneous-process-b");
        if (string.IsNullOrWhiteSpace(resultPath)
            || string.IsNullOrWhiteSpace(readyPath)
            || string.IsNullOrWhiteSpace(processA)
            || string.IsNullOrWhiteSpace(processB)) return false;
        var duration = int.TryParse(ReadArgument(args, "--simultaneous-self-test-duration"), out var durationSeconds)
            ? Math.Clamp(durationSeconds, 5, 120)
            : 20;
        arguments = new SimultaneousSourceSelfTestArguments(processA, processB, resultPath, readyPath, duration);
        return true;
    }

    private readonly record struct SelfTestArguments(
        string AudioPath,
        string ResultPath,
        string? ExpectedText,
        string? ModelPath,
        LiveSubtitle.Core.InferenceBackend Backend);

    private readonly record struct SoakSelfTestArguments(
        string AudioPath,
        string ResultPath,
        string ReadyPath,
        int DurationSeconds,
        int SilenceSeconds);

    private readonly record struct CaptureSelfTestArguments(
        LiveSubtitle.Core.AudioSourceKind SourceKind,
        string? ProcessPath,
        string ResultPath,
        string ReadyPath,
        int DurationSeconds,
        string? ExpectedText,
        string? ForbiddenText);

    private readonly record struct SimultaneousSourceSelfTestArguments(
        string ProcessA,
        string ProcessB,
        string ResultPath,
        string ReadyPath,
        int DurationSeconds);
}
