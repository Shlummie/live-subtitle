using LiveSubtitle.Core;

namespace LiveSubtitle.App.ViewModels;

public sealed record OverlayMonitorOption(string Id, string Name, bool IsPrimary);

public sealed record OverlayAnchorOption(OverlayAnchor Value, string Name);
