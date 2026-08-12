using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using LiveSubtitle.App.Services;
using LiveSubtitle.App.ViewModels;

namespace LiveSubtitle.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    private readonly OverlayWindow overlay;
    private readonly SanitizedLogger logger;
    private GlobalHotkeyService? hotkeys;
    private TrayIconService? tray;
    private bool exiting;

    public MainWindow(MainViewModel viewModel, OverlayWindow overlay, SanitizedLogger logger)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.overlay = overlay;
        this.logger = logger;
        DataContext = viewModel;
        SourceInitialized += OnSourceInitialized;
        SourceInitialized += (_, _) => ThemeManager.ApplyWindowChrome(this, viewModel.Theme);
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += (_, _) => Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized) Hide();
        };
        viewModel.ThemeChanged += (_, theme) =>
        {
            ThemeManager.Apply(theme);
            ThemeManager.ApplyWindowChrome(this, theme);
        };
    }

    private void OnUserPreferenceChanged(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            ThemeManager.Apply(viewModel.Theme);
            ThemeManager.ApplyWindowChrome(this, viewModel.Theme);
        });

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        hotkeys = new GlobalHotkeyService(this);
        hotkeys.Pressed += OnHotkeyPressed;
        viewModel.HotkeysChanged += OnHotkeysChanged;
        RegisterHotkeys();
        tray = new TrayIconService(viewModel, ShowControlCenter, ExitAsync);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (viewModel.OverlayVisible) overlay.Show();
        if (viewModel.ShouldShowFirstRun)
        {
            Dispatcher.BeginInvoke(() =>
            {
                var firstRun = new FirstRunWindow(viewModel) { Owner = this };
                firstRun.SourceInitialized += async (_, _) => await logger.InfoAsync(
                    "first-run-window",
                    $"visible={firstRun.IsVisible}; handle={new WindowInteropHelper(firstRun).Handle}");
                firstRun.ShowDialog();
            });
        }
    }

    private void RegisterHotkeys()
    {
        try
        {
            hotkeys?.Register(viewModel.CurrentHotkeyProfile);
        }
        catch (Exception exception)
        {
            viewModel.ReportStatus("Shortcut conflict", exception.Message);
        }
    }

    private void OnHotkeysChanged(object? sender, HotkeyApplyEventArgs e)
    {
        try
        {
            hotkeys?.Register(e.Profile);
        }
        catch (Exception exception)
        {
            e.Error = exception.Message;
        }
    }

    private void OnHotkeyPressed(object? sender, HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.ToggleListening: viewModel.ToggleListeningCommand.Execute(null); break;
            case HotkeyAction.ToggleOverlay: viewModel.ToggleOverlayCommand.Execute(null); break;
            case HotkeyAction.ToggleOverlayLock: viewModel.ToggleOverlayLockCommand.Execute(null); break;
            case HotkeyAction.NextProfile: viewModel.CycleProfile(); break;
            case HotkeyAction.SaveSession: viewModel.SaveSessionCommand.Execute(null); break;
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (exiting) return;
        e.Cancel = true;
        if (viewModel.IsListening && viewModel.MinimizeToTray)
        {
            Hide();
            tray?.NotifyMinimized();
            return;
        }
        _ = ExitAsync();
    }

    public void ShowControlCenter()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Focus();
    }

    public async Task ExitAsync()
    {
        if (exiting) return;
        exiting = true;
        await viewModel.DisposeAsync();
        viewModel.HotkeysChanged -= OnHotkeysChanged;
        hotkeys?.Dispose();
        tray?.Dispose();
        overlay.Close();
        Close();
        Application.Current.Shutdown();
    }
}
