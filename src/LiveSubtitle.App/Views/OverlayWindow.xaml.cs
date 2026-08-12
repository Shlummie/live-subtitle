using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using LiveSubtitle.App.ViewModels;
using LiveSubtitle.Core;
using Forms = System.Windows.Forms;

namespace LiveSubtitle.App.Views;

public partial class OverlayWindow : Window
{
    private const int ExtendedStyleIndex = -20;
    private const long ToolWindowStyle = 0x00000080L;
    private const long TransparentStyle = 0x00000020L;
    private const uint DisplayAffinityNone = 0;
    private const uint DisplayAffinityExcludeFromCapture = 0x00000011;
    private const uint SetWindowPositionNoSize = 0x0001;
    private const uint SetWindowPositionNoZOrder = 0x0004;
    private const uint SetWindowPositionNoActivate = 0x0010;
    private readonly MainViewModel viewModel;
    private bool positioned;
    private bool applyingProfile;

    public OverlayWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
        SourceInitialized += (_, _) => ApplyProfile();
        SizeChanged += OnSizeChanged;
        Closed += (_, _) => Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        viewModel.OverlayProfileChanged += (_, _) => Dispatcher.BeginInvoke(ApplyProfile);
        viewModel.OverlayVisibilityChanged += (_, visible) => Dispatcher.BeginInvoke(() =>
        {
            if (visible) Show(); else Hide();
        });
    }

    public void ApplyProfile()
    {
        if (applyingProfile) return;
        applyingProfile = true;
        var profile = viewModel.CurrentOverlayProfile;
        try
        {
            Width = Math.Clamp(profile.Width, 320, 2400);
            ResizeMode = profile.Locked ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
            OverlaySurface.CornerRadius = new CornerRadius(profile.CornerRadius);
            OverlaySurface.BorderThickness = new Thickness(profile.OutlineThickness);
            OverlaySurface.BorderBrush = ParseBrush(profile.OutlineColorHex, 1);
            OverlaySurface.Background = ParseBrush(profile.BackgroundColorHex, profile.BackgroundOpacity);
            OverlaySurface.Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = profile.ShadowBlurRadius,
                Opacity = profile.ShadowOpacity,
                ShadowDepth = profile.ShadowDepth,
                RenderingBias = RenderingBias.Performance
            };

            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                var style = GetWindowLongPtr(handle, ExtendedStyleIndex).ToInt64() | ToolWindowStyle;
                if (profile.Locked && profile.ClickThroughWhenLocked) style |= TransparentStyle;
                else style &= ~TransparentStyle;
                SetWindowLongPtr(handle, ExtendedStyleIndex, new IntPtr(style));
                SetWindowDisplayAffinity(handle, profile.CaptureMode == OverlayCaptureMode.Exclude
                    ? DisplayAffinityExcludeFromCapture
                    : DisplayAffinityNone);
                UpdateLayout();
                ApplyPlacement(handle, profile);
            }
        }
        finally
        {
            applyingProfile = false;
        }
    }

    private void ApplyPlacement(IntPtr handle, OverlayProfile profile)
    {
        var screen = Forms.Screen.AllScreens.FirstOrDefault(candidate => candidate.DeviceName == profile.MonitorId)
                     ?? Forms.Screen.PrimaryScreen;
        if (screen is null || !GetWindowRect(handle, out var window)) return;

        var work = screen.WorkingArea;
        var width = Math.Max(1, window.Right - window.Left);
        var height = Math.Max(1, window.Bottom - window.Top);
        var scale = width / Math.Max(1, ActualWidth);
        var inset = Math.Max(8, (int)Math.Round(16 * scale));
        var availableX = Math.Max(0, work.Width - width);
        var availableY = Math.Max(0, work.Height - height);
        var left = profile.Anchor switch
        {
            OverlayAnchor.BottomLeft => work.Left + inset,
            OverlayAnchor.BottomRight => work.Right - width - inset,
            OverlayAnchor.Custom => work.Left + (int)Math.Round(availableX * profile.X),
            _ => work.Left + availableX / 2
        };
        var top = profile.Anchor switch
        {
            OverlayAnchor.TopCenter => work.Top + inset,
            OverlayAnchor.Custom => work.Top + (int)Math.Round(availableY * profile.Y),
            _ => work.Bottom - height - inset
        };

        left = Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - width));
        top = Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - height));
        SetWindowPos(
            handle,
            IntPtr.Zero,
            left,
            top,
            0,
            0,
            SetWindowPositionNoSize | SetWindowPositionNoZOrder | SetWindowPositionNoActivate);
        positioned = true;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (viewModel.OverlayLocked || e.LeftButton != MouseButtonState.Pressed) return;
        DragMove();
        PersistPlacement(snap: true);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (applyingProfile || !positioned) return;
        if (viewModel.OverlayLocked)
        {
            if (viewModel.CurrentOverlayProfile.Anchor != OverlayAnchor.Custom) ApplyProfile();
            return;
        }
        PersistPlacement(snap: false);
    }

    private void PersistPlacement(bool snap)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var window)) return;
        var rectangle = new System.Drawing.Rectangle(
            window.Left,
            window.Top,
            Math.Max(1, window.Right - window.Left),
            Math.Max(1, window.Bottom - window.Top));
        var screen = Forms.Screen.FromRectangle(rectangle);
        var work = screen.WorkingArea;
        var left = rectangle.Left;
        var top = rectangle.Top;

        if (snap)
        {
            var scale = rectangle.Width / Math.Max(1, ActualWidth);
            var inset = Math.Max(8, (int)Math.Round(16 * scale));
            var threshold = Math.Max(12, (int)Math.Round(24 * scale));
            left = Snap(
                left,
                [work.Left + inset, work.Left + Math.Max(0, work.Width - rectangle.Width) / 2, work.Right - rectangle.Width - inset],
                threshold);
            top = Snap(top, [work.Top + inset, work.Bottom - rectangle.Height - inset], threshold);
            SetWindowPos(
                handle,
                IntPtr.Zero,
                left,
                top,
                0,
                0,
                SetWindowPositionNoSize | SetWindowPositionNoZOrder | SetWindowPositionNoActivate);
        }

        var availableX = Math.Max(1, work.Width - rectangle.Width);
        var availableY = Math.Max(1, work.Height - rectangle.Height);
        var x = Math.Clamp((double)(left - work.Left) / availableX, 0, 1);
        var y = Math.Clamp((double)(top - work.Top) / availableY, 0, 1);
        viewModel.UpdateOverlayPlacement(screen.DeviceName, x, y, ActualWidth);
    }

    private static int Snap(int value, IReadOnlyList<int> points, int threshold)
    {
        var nearest = points.OrderBy(point => Math.Abs(point - value)).First();
        return Math.Abs(nearest - value) <= threshold ? nearest : value;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        viewModel.RefreshOverlayMonitors();
        positioned = false;
        ApplyProfile();
    });

    private static SolidColorBrush ParseBrush(string value, double opacity)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(value);
            color.A = (byte)Math.Clamp(color.A * opacity, 0, 255);
            return new SolidColorBrush(color);
        }
        catch { return new SolidColorBrush(Color.FromArgb((byte)(210 * opacity), 22, 26, 32)); }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr window, int index);
    private static IntPtr GetWindowLongPtr(IntPtr window, int index) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(window, index) : new IntPtr(GetWindowLong32(window, index));
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr window, int index, int value);
    private static IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(window, index, value) : new IntPtr(SetWindowLong32(window, index, value.ToInt32()));
    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
