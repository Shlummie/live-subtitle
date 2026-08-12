using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using LiveSubtitle.Core;

namespace LiveSubtitle.App.Services;

public enum HotkeyAction
{
    ToggleListening = 1,
    ToggleOverlay = 2,
    ToggleOverlayLock = 3,
    SaveSession = 4,
    NextProfile = 5
}

public sealed class GlobalHotkeyService : IDisposable
{
    private readonly IntPtr handle;
    private readonly HwndSource source;
    private readonly HashSet<int> registered = [];
    private readonly Dictionary<int, (uint Modifiers, uint Key)> activeBindings = [];

    public GlobalHotkeyService(Window window)
    {
        handle = new WindowInteropHelper(window).EnsureHandle();
        source = HwndSource.FromHwnd(handle) ?? throw new InvalidOperationException("Cannot attach global shortcuts.");
        source.AddHook(WindowHook);
    }

    public event EventHandler<HotkeyAction>? Pressed;

    public void Register(HotkeyProfile profile)
    {
        var entries = new[]
        {
            (HotkeyAction.ToggleListening, profile.ToggleListening),
            (HotkeyAction.ToggleOverlay, profile.ToggleOverlay),
            (HotkeyAction.ToggleOverlayLock, profile.ToggleOverlayLock),
            (HotkeyAction.NextProfile, profile.NextProfile),
            (HotkeyAction.SaveSession, profile.SaveSession)
        };

        var parsed = entries
            .Select(entry => (entry.Item1, Text: entry.Item2, Binding: Parse(entry.Item2)))
            .ToList();
        var duplicate = parsed
            .GroupBy(entry => entry.Binding)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"{duplicate.First().Text} is assigned to more than one action.");
        }

        var previous = activeBindings.ToDictionary(entry => entry.Key, entry => entry.Value);
        UnregisterAll();
        try
        {
            foreach (var (action, text, binding) in parsed)
            {
                var (modifiers, key) = binding;
                if (!RegisterHotKey(handle, (int)action, modifiers | 0x4000, key))
                {
                    throw new InvalidOperationException($"{text} is already used by Windows or another application.");
                }
                registered.Add((int)action);
                activeBindings[(int)action] = binding;
            }
        }
        catch
        {
            UnregisterAll();
            foreach (var (id, binding) in previous)
            {
                if (!RegisterHotKey(handle, id, binding.Modifiers | 0x4000, binding.Key)) continue;
                registered.Add(id);
                activeBindings[id] = binding;
            }
            throw;
        }
    }

    private IntPtr WindowHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312 && Enum.IsDefined(typeof(HotkeyAction), wParam.ToInt32()))
        {
            handled = true;
            Pressed?.Invoke(this, (HotkeyAction)wParam.ToInt32());
        }
        return IntPtr.Zero;
    }

    private static (uint Modifiers, uint Key) Parse(string value)
    {
        uint modifiers = 0;
        Key key = Key.None;
        foreach (var part in value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL": modifiers |= 0x0002; break;
                case "ALT": modifiers |= 0x0001; break;
                case "SHIFT": modifiers |= 0x0004; break;
                case "WIN" or "WINDOWS": modifiers |= 0x0008; break;
                default:
                    key = (Key)new KeyConverter().ConvertFromInvariantString(part)!;
                    break;
            }
        }
        if (key == Key.None || modifiers == 0) throw new FormatException($"Invalid shortcut: {value}");
        return (modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
    }

    private void UnregisterAll()
    {
        foreach (var id in registered) UnregisterHotKey(handle, id);
        registered.Clear();
        activeBindings.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
        source.RemoveHook(WindowHook);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
