using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using LiveSubtitle.Core;
using Microsoft.Win32;
using ThemeMode = LiveSubtitle.Core.ThemeMode;
using MediaColor = System.Windows.Media.Color;

namespace LiveSubtitle.App.Services;

public static class ThemeManager
{
    public static void Apply(ThemeMode requested)
    {
        var mode = requested == ThemeMode.System
            ? IsSystemLightTheme() ? ThemeMode.Light : ThemeMode.Dark
            : requested;
        if (mode == ThemeMode.HighContrast || SystemParameters.HighContrast)
        {
            Set("WindowBrush", SystemColors.WindowColor);
            Set("SidebarBrush", SystemColors.ControlColor);
            Set("SurfaceBrush", SystemColors.ControlColor);
            Set("SurfaceRaisedBrush", SystemColors.ControlLightColor);
            Set("SurfaceHoverBrush", SystemColors.HighlightColor);
            Set("BorderBrush", SystemColors.WindowTextColor);
            Set("TextBrush", SystemColors.WindowTextColor);
            Set("TextSecondaryBrush", SystemColors.WindowTextColor);
            Set("TextMutedBrush", SystemColors.GrayTextColor);
            Set("AccentBrush", SystemColors.HighlightColor);
            Set("AccentHoverBrush", SystemColors.HighlightColor);
            Set("AccentSubtleBrush", SystemColors.ControlLightColor);
            Set("SuccessBrush", SystemColors.HighlightColor);
            Set("WarningBrush", SystemColors.WindowTextColor);
            Set("ErrorBrush", SystemColors.WindowTextColor);
            Set("FocusBrush", SystemColors.HighlightColor);
            return;
        }

        var palette = mode == ThemeMode.Light
            ? new Dictionary<string, string>
            {
                ["WindowBrush"] = "#F5F7FA",
                ["SidebarBrush"] = "#ECEFF3",
                ["SurfaceBrush"] = "#FFFFFF",
                ["SurfaceRaisedBrush"] = "#F1F4F8",
                ["SurfaceHoverBrush"] = "#E4EAF1",
                ["BorderBrush"] = "#CCD4DE",
                ["TextBrush"] = "#17202B",
                ["TextSecondaryBrush"] = "#455467",
                ["TextMutedBrush"] = "#66778C",
                ["AccentBrush"] = "#276BD2",
                ["AccentHoverBrush"] = "#1F5CB8",
                ["AccentSubtleBrush"] = "#DDEAFF",
                ["SuccessBrush"] = "#168A56",
                ["WarningBrush"] = "#8B5E00",
                ["ErrorBrush"] = "#B4232C",
                ["FocusBrush"] = "#276BD2"
            }
            : new Dictionary<string, string>
            {
                ["WindowBrush"] = "#0D1117",
                ["SidebarBrush"] = "#11161E",
                ["SurfaceBrush"] = "#171D26",
                ["SurfaceRaisedBrush"] = "#1D2530",
                ["SurfaceHoverBrush"] = "#25303D",
                ["BorderBrush"] = "#2A3441",
                ["TextBrush"] = "#F4F7FB",
                ["TextSecondaryBrush"] = "#B8C3D1",
                ["TextMutedBrush"] = "#8491A2",
                ["AccentBrush"] = "#4F8EF7",
                ["AccentHoverBrush"] = "#6DA2F9",
                ["AccentSubtleBrush"] = "#203A66",
                ["SuccessBrush"] = "#49D493",
                ["WarningBrush"] = "#F2B84B",
                ["ErrorBrush"] = "#F47B82",
                ["FocusBrush"] = "#8CB7FF"
            };
        foreach (var (key, value) in palette) Set(key, (MediaColor)ColorConverter.ConvertFromString(value));
    }

    public static void ApplyWindowChrome(Window window, ThemeMode requested)
    {
        var mode = requested == ThemeMode.System
            ? IsSystemLightTheme() ? ThemeMode.Light : ThemeMode.Dark
            : requested;
        var dark = mode == ThemeMode.Dark && !SystemParameters.HighContrast ? 1 : 0;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        if (DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
        }
    }

    private static void Set(string key, MediaColor color) =>
        Application.Current.Resources[key] = new SolidColorBrush(color);

    private static bool IsSystemLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return Convert.ToInt32(key?.GetValue("AppsUseLightTheme", 0)) != 0;
        }
        catch { return false; }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);
}
