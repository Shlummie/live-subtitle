using LiveSubtitle.Core;

namespace LiveSubtitle.App.ViewModels;

public sealed class HotkeyApplyEventArgs(HotkeyProfile profile) : EventArgs
{
    public HotkeyProfile Profile { get; } = profile;
    public string? Error { get; set; }
}
