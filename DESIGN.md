# LiveSubtitle Visual System

This document records the implemented and visually verified interface, not an aspirational design. The release UI was reviewed at 125% Windows scaling in the Live-page capture (`artifacts/qa/main-live-accessible.png`), the responsive appearance page (`artifacts/qa/setting-help-appearance.png`), the contextual-help capture (`artifacts/qa/setting-help-hover-final.png`), and the four-step first-run flow.

## Design direction

LiveSubtitle follows the density and restraint of PowerToys and Windows Settings: persistent navigation, direct status language, modest surface elevation, and one dominant action per page. The interface avoids decorative imagery so listening state, source health, privacy, and recovery actions stay primary.

## Structure

- The control center opens at 1260 × 800 DIPs with a 1060 × 680 minimum.
- A 248-DIP left rail holds product identity, eight pages, selected-state feedback, and the persistent listening/privacy indicator.
- Page content uses a 34/44-DIP outer inset, 12-DIP cards, 18-DIP card padding, and a restrained two-column layout where comparison is useful.
- The Live page places session/profile controls first, then a 2.1:1 transcript/source-health split.
- The Appearance page keeps monitor, snap, typography, and depth controls in a keyboard-scrollable column beside a responsive representation of the real overlay.
- The Recognition page presents audio sensitivity as a 0–100 quiet-audio control with plain-language noise-rejection guidance and an explicit per-profile save action.
- A restrained circular **i** button sits beside every setting label. Its tooltip explains what the setting changes, when it applies, and any important tradeoff without permanently increasing page density.
- The 900-DIP overlay sizes to caption content and uses an 18 × 12 inset, 12-DIP radius, and a compact live/edit-state header.

## Typography

- UI: `Segoe UI Variable Text`, falling back to `Segoe UI`.
- Display: `Segoe UI Variable Display`, falling back to `Segoe UI`.
- Page title: 30/36, semibold.
- Section title: 18, semibold.
- Body: 13/20; labels: 12, semibold; secondary metadata: 10–11.
- Live transcript: 18/26 in the control center. Overlay size, family, weight, line count, width, and labels are user controlled.

## Color

Dark is the default:

| Role | Value |
| --- | --- |
| Window | `#0D1117` |
| Sidebar | `#11161E` |
| Surface | `#171D26` |
| Raised surface | `#1D2530` |
| Border | `#2A3441` |
| Primary text | `#F4F7FB` |
| Secondary text | `#B8C3D1` |
| Muted text | `#8491A2` |
| Accent | `#4F8EF7` |
| Success/listening | `#49D493` |
| Warning | `#F2B84B` |
| Error | `#F47B82` |
| Keyboard focus | `#8CB7FF` |

The light palette is a native companion rather than an inversion. System mode follows Windows' app theme. Explicit High Contrast mode and the Windows high-contrast setting replace the palette with `SystemColors`, including system highlight and window-text colors.

## States and motion

- Listening is communicated by text, green status dot, main action label, overlay header, and tray state; color is never the sole signal.
- Draft words are visually subdued until the transcript stabilizer promotes them to the stable prefix.
- Source cards pair label, state text, meter, and a specific recovery detail.
- Duplicate capture routes use a bordered warning banner and explanatory text.
- Focused controls receive a two-pixel focus border. Disabled actions lower opacity but keep their labels visible.
- Motion is limited to short opacity pulses and native popup fades. **Reduce motion** disables nonessential status animation.

## Overlay

The overlay is transparent, always on top, monitor-aware, and independent from the control center. Users can select a monitor and anchor, drag or resize in edit mode, snap to common edges, set opacity/background/text/outline/shadow, control labels and maximum lines, and then lock the window into click-through mode. Position is stored as normalized monitor-relative coordinates so it survives resolution changes. Optional display-affinity control excludes it from supported screen capture paths.

The app never injects into another process. This makes the overlay appropriate for desktop, windowed, and borderless-fullscreen use but intentionally excludes true exclusive-fullscreen games.

## Accessibility verification

- Native WPF controls retain keyboard patterns, tab order, and screen-reader peers.
- Setting-help buttons open from hover, keyboard focus, or click; each exposes an `About …` accessible name and the same explanation through UI Automation help text.
- Navigation and interactive page controls have explicit accessible names.
- Decorative icons and labels are removed from the tab sequence.
- The selected profile is exposed by name, hidden pages do not create hidden tab stops, and status is expressed in text.
- Per-monitor-v2 DPI awareness is declared in the application manifest.
- High contrast uses Windows system colors, and the interface remains usable without animation.
- Dark, light, system, and high-contrast changes update both content and native window chrome; Reduce motion disables combo-popup fades.

The final automated UI Automation audit exposed every visible focusable control by a friendly name across all eight pages and found zero hidden-page tab stops. A separate two-monitor UI Automation audit moved the live overlay to the secondary display, verified containment, then verified top-center and bottom-center anchors on the primary display. Visual captures and machine-readable results are stored under `artifacts/qa`.
