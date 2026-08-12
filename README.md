# LiveSubtitle

LiveSubtitle is a local-first Windows 10 caption utility. It captures a selected output device, microphone/input, application process tree, or several independently labeled sources and turns English speech into an always-on-top subtitle overlay.

The application is native x64 WPF, targets Windows 10 version 2004 (build 19041) or newer, and ships as one compressed, self-contained `LiveSubtitle.exe`. It does not require installation or a separate .NET runtime.

## Privacy model

- Audio is normalized and processed in bounded memory; raw audio is never written to disk.
- Recognition runs locally through Whisper.net/whisper.cpp. Auto backend selection tries Vulkan first and AVX2 CPU second.
- Live captions are ephemeral. Transcript text is persisted only after **Save this session** is enabled or **Save now** is explicitly used.
- Merely opening or searching Saved Sessions does not create the history database.
- Logs are rotating and sanitized; they do not include transcript text.
- There are no accounts, telemetry, analytics, cloud inference, or background uploads.
- The only network operation is an explicit model download from a pinned HTTPS URL. A `.partial` download resumes after interruption and is promoted only after its SHA-256 hash passes.

Runtime data is stored under `%LocalAppData%\LiveSubtitle`:

- `settings.json` — versioned settings and named profiles
- `Models` — separately downloaded Whisper models
- `Logs` — sanitized rotating logs
- `history.sqlite3` — created only by an explicit session save

## Getting started

1. Launch `LiveSubtitle.exe`.
2. Complete first-run setup: review privacy, download `base.en`, select/test sources, position the overlay, and run the backend check.
3. Select **Start listening** or press `Ctrl+Alt+Space`.
4. Press `Ctrl+Alt+O` to show or hide the overlay. Unlock it before dragging or resizing it; lock it for click-through use.
5. Press `Ctrl+Alt+P` to switch to the next named profile from any application.
6. Enable **Save this session** only when you want recognized text retained. Unsaved text is cleared when listening ends or the app exits.

The control center contains Live, Audio Sources, Subtitle Appearance, Recognition, Hotkeys, Saved Sessions, General, and Diagnostics pages. Every configurable setting has a small **i** button; hover it, focus it with the keyboard, or select it to see a plain-language explanation. Named Gaming, Videos, Calls, and user-created profiles can be switched from the UI, tray menu, or configurable next-profile hotkey—even while listening. Profile changes safely restart the local capture/engine pipeline without erasing the current in-memory transcript.

## Capture modes

- Selected Windows output endpoint using WASAPI loopback
- Selected microphone or other input endpoint
- Selected application process tree on Windows 10 version 2004+
- Multiple simultaneous sources with independent labels, colors, gain, meters, and recognition state

The Audio Sources page includes a model-free five-second route test so meters and inactive/recovery states can be checked before transcription starts.

Selecting whole-computer audio and an application contained within it produces a duplicate-route warning. Application matching uses executable path plus descendant processes and automatically reattaches after a process restart.

Browser audio may span helper processes or tabs. Protected DRM audio may be unavailable. Unusual routing can fall back to whole-output capture. The overlay supports desktop, windowed, and borderless-fullscreen applications; it intentionally does not inject into true exclusive-fullscreen games. Monitor, snap anchor, dragged position, width, typography, source labels, colors, outline, shadow, background, opacity, line count, click-through state, and screen-capture visibility are stored per profile.

## Models

Models are intentionally separate downloads:

| UI tier | Model | Purpose |
| --- | --- | --- |
| Fast | `tiny.en` | Lowest resource use |
| Recommended | `base.en` | Default balance |
| Accurate | `small.en` | Higher accuracy |
| Max GPU | `large-v3-turbo-q5_0` | Highest GPU tier |

The shipped Silero VAD model detects speech locally. Recognition defaults to the Aggressive preset: 250 ms draft cadence, about 450 ms silence finalization, 500 ms context overlap, and a 15-second utterance cap. Balanced, Stable, and fully custom timing/VAD configurations are available. A per-profile 0–100 audio-sensitivity control trades quiet-dialogue pickup against background-noise rejection. Whisper blank/no-speech sentinel output is suppressed instead of displayed or saved.

## Build and test

Requirements: Windows 10 build 19041+, .NET 10 SDK, and the Windows desktop targeting pack.

```powershell
dotnet restore LiveSubtitle.slnx
dotnet build LiveSubtitle.slnx -c Release
dotnet test tests\LiveSubtitle.Tests\LiveSubtitle.Tests.csproj -c Release
```

Run the development build:

```powershell
dotnet run --project src\LiveSubtitle.App\LiveSubtitle.App.csproj -c Release
```

Publish the self-contained single-file portable app:

```powershell
dotnet publish src\LiveSubtitle.App\LiveSubtitle.App.csproj -c Release -r win-x64 --self-contained true --no-restore -o artifacts\publish-single
```

Build the per-user installer after publishing:

```powershell
ISCC.exe installer\LiveSubtitle.iss
```

The portable output contains only `LiveSubtitle.exe`. The installer writes that same executable under `%LocalAppData%\Programs\LiveSubtitle`, creates a Start menu shortcut, and offers an optional desktop shortcut. Uninstall preserves `%LocalAppData%\LiveSubtitle` unless **Delete all LiveSubtitle data** is explicitly checked.

## Verification

The committed unit suite covers resampling, bounded ring buffers, VAD transitions, draft stabilization, chronological source merge, settings migration/profile round-trip, model hashing, and SRT/VTT formatting. Run the tests on the target Windows machine before distributing a build.

Internal hardware-specific acceptance artifacts and benchmark results are intentionally not included in this public repository, so this README does not claim those private measurements as reproducible evidence. Real latency and accuracy depend on the selected Whisper model, backend, device routing, audio quality, language, and hardware.

## Troubleshooting

- **No model:** Open Recognition and download/verify a model.
- **Vulkan cannot load:** Choose CPU or leave Auto enabled; the engine restarts on the AVX2 CPU backend.
- **Device unplugged/default changed:** Keep the source enabled. LiveSubtitle retries and reconnects when the endpoint returns.
- **Application is inactive:** Start the configured executable. Its source automatically reattaches after restart.
- **Browser tab or DRM audio is missing:** Add whole-computer output as the fallback source.
- **Overlay does not appear over a game:** Use windowed or borderless-fullscreen mode. Exclusive-fullscreen injection is intentionally unsupported.
- **Hotkey rejected:** The shortcut is already registered by Windows or another app; choose a different combination.

See `PRODUCT.md` for product decisions, `DESIGN.md` for the verified visual system, and `THIRD_PARTY_NOTICES.md` for runtime dependency notices.

## License

Original source is MIT licensed. Whisper models, ONNX Runtime, audio backends, installer tooling, and other dependencies retain their upstream licenses and distribution terms; see `THIRD_PARTY_NOTICES.md` before redistributing a packaged build.
