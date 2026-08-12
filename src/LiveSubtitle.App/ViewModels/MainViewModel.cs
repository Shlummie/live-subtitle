using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using LiveSubtitle.App.Audio;
using LiveSubtitle.App.Engine;
using LiveSubtitle.App.Services;
using LiveSubtitle.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using ThemeMode = LiveSubtitle.Core.ThemeMode;

namespace LiveSubtitle.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly AppPaths paths;
    private readonly SettingsService settingsService;
    private readonly ModelDownloadService modelDownloads;
    private readonly HistoryService history;
    private readonly SanitizedLogger logger;
    private readonly AudioDeviceService deviceService = new();
    private readonly SemaphoreSlim engineRecoveryGate = new(1, 1);
    private AppSettings settings;
    private UserProfile activeProfile;
    private AudioCaptureCoordinator? capture;
    private EngineClient? engine;
    private CancellationTokenSource? sessionCancellation;
    private Task? audioPump;
    private CancellationTokenSource? sourceTestCancellation;
    private string sessionId = string.Empty;
    private DateTimeOffset sessionStartedUtc;
    private readonly List<TranscriptSegment> currentSegments = [];
    private int selectedNavigationIndex;
    private bool isListening;
    private bool isBusy;
    private bool saveThisSession;
    private bool overlayVisible = true;
    private string statusText = "Ready to listen";
    private string statusDetail = "Audio stays on this PC.";
    private string duplicateRouteWarning = string.Empty;
    private UserProfile? selectedProfile;
    private ModelViewModel? selectedModel;
    private InferenceBackend backend;
    private RecognitionPreset recognitionPreset;
    private bool applyingRecognitionPreset;
    private int draftIntervalMs;
    private int finalizeSilenceMs;
    private int windowOverlapMs;
    private int maxUtteranceSeconds;
    private float vadThreshold;
    private float noiseFloor;
    private string customVocabulary = string.Empty;
    private double overlayWidth;
    private int overlayMaxLines;
    private string overlayFontFamily = string.Empty;
    private double overlayFontSize;
    private int overlayFontWeight;
    private string overlayTextColor = string.Empty;
    private string overlayDraftColor = string.Empty;
    private string overlayBackgroundColor = string.Empty;
    private string overlayOutlineColor = string.Empty;
    private double overlayOpacity;
    private double overlayCornerRadius;
    private double overlayOutlineThickness;
    private double overlayShadowOpacity;
    private double overlayShadowBlurRadius;
    private double overlayShadowDepth;
    private string overlayMonitorId = string.Empty;
    private OverlayAnchor overlayAnchorMode;
    private double overlayX;
    private double overlayY;
    private bool showSourceLabels;
    private bool overlayLocked;
    private bool clickThroughWhenLocked;
    private OverlayCaptureMode overlayCaptureMode;
    private ThemeMode theme;
    private bool startWithWindows;
    private bool minimizeToTray;
    private bool reduceMotion;
    private string hotkeyListening = string.Empty;
    private string hotkeyOverlay = string.Empty;
    private string hotkeyLock = string.Empty;
    private string hotkeyNextProfile = string.Empty;
    private string hotkeySave = string.Empty;
    private string historyQuery = string.Empty;
    private TranscriptSession? selectedSavedSession;
    private string benchmarkResult = "Not run";
    private string sourceTestStatus = "Play audio or speak while the five-second test is running.";
    private string newProfileName = string.Empty;
    private int engineRecoveryCount;

    public MainViewModel(
        AppSettings settings,
        AppPaths paths,
        SettingsService settingsService,
        ModelDownloadService modelDownloads,
        HistoryService history,
        SanitizedLogger logger)
    {
        this.settings = settings;
        this.paths = paths;
        this.settingsService = settingsService;
        this.modelDownloads = modelDownloads;
        this.history = history;
        this.logger = logger;
        activeProfile = settings.Profiles.First(profile => profile.Id == settings.ActiveProfileId);

        Navigation = new ObservableCollection<NavigationItemViewModel>
        {
            new("Live", "M3,3 H13 V13 H3 Z M6,6 H10 V10 H6 Z"),
            new("Audio sources", "M2,6 H5 L9,2 V14 L5,10 H2 Z M11,5 C13,7 13,9 11,11"),
            new("Subtitle appearance", "M2,3 H14 V12 H8 L5,15 V12 H2 Z M5,6 H11 M5,9 H9"),
            new("Recognition", "M8,2 V8 M5,5 V8 A3,3 0 0 0 11,8 V5 M4,13 H12"),
            new("Hotkeys", "M2,4 H14 V12 H2 Z M4,7 H5 M7,7 H8 M10,7 H12 M4,10 H10"),
            new("Saved sessions", "M3,2 H11 L14,5 V14 H3 Z M5,8 H12 M5,11 H10"),
            new("General", "M8,2 L9,4 L11,3 L13,5 L12,7 L14,8 L13,11 L11,10 L10,13 H7 L6,10 L4,11 L2,9 L4,7 L3,5 L5,3 L7,4 Z"),
            new("Diagnostics", "M2,13 H14 M4,11 V7 M8,11 V3 M12,11 V6")
        };

        Profiles = new ObservableCollection<UserProfile>(settings.Profiles);
        AudioSources = [];
        Endpoints = [];
        Processes = [];
        Captions = [];
        OverlayCaptions = [];
        OverlayMonitors = [];
        Models = new ObservableCollection<ModelViewModel>(ModelCatalog.Models.Select(model => new ModelViewModel(model)));
        SavedSessions = [];
        Diagnostics = [];

        ToggleListeningCommand = new AsyncRelayCommand(ToggleListeningAsync, () => !IsBusy);
        ToggleOverlayCommand = new RelayCommand(() => OverlayVisible = !OverlayVisible);
        ToggleOverlayLockCommand = new RelayCommand(() => OverlayLocked = !OverlayLocked);
        SaveSessionCommand = new AsyncRelayCommand(SaveCurrentSessionAsync, () => currentSegments.Count > 0);
        AddOutputSourceCommand = new RelayCommand(() => AddSource(AudioSourceKind.OutputDevice), () => CanEditSources);
        AddInputSourceCommand = new RelayCommand(() => AddSource(AudioSourceKind.InputDevice), () => CanEditSources);
        AddProcessSourceCommand = new RelayCommand(() => AddSource(AudioSourceKind.Process), () => CanEditSources);
        RemoveSourceCommand = new RelayCommand(
            parameter => RemoveSource(parameter as AudioSourceViewModel),
            _ => CanEditSources);
        RefreshAudioCommand = new AsyncRelayCommand(RefreshAudioAsync);
        TestSourcesCommand = new AsyncRelayCommand(TestSourcesAsync, () => !IsListening && !IsBusy);
        DownloadModelCommand = new AsyncRelayCommand(DownloadSelectedModelAsync, () => SelectedModel is not null);
        VerifyModelsCommand = new AsyncRelayCommand(RefreshModelsAsync);
        RunBenchmarkCommand = new AsyncRelayCommand(RunBenchmarkAsync, () => !IsListening);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync);
        ImportSettingsCommand = new AsyncRelayCommand(ImportSettingsAsync);
        ExportSettingsCommand = new AsyncRelayCommand(ExportSettingsAsync);
        SearchHistoryCommand = new AsyncRelayCommand(LoadHistoryAsync);
        CopySavedSessionCommand = new AsyncRelayCommand(CopySavedSessionAsync, () => SelectedSavedSession is not null);
        ExportSavedSessionCommand = new AsyncRelayCommand(ExportSavedSessionAsync, () => SelectedSavedSession is not null);
        DeleteSavedSessionCommand = new AsyncRelayCommand(DeleteSavedSessionAsync, () => SelectedSavedSession is not null);
        CreateProfileCommand = new AsyncRelayCommand(CreateProfileAsync, () => !string.IsNullOrWhiteSpace(NewProfileName));
        DeleteProfileCommand = new AsyncRelayCommand(DeleteSelectedProfileAsync, () => CanDeleteSelectedProfile);
        OpenDataFolderCommand = new RelayCommand(() => OpenFolder(paths.Root));
        OpenLogsFolderCommand = new RelayCommand(() => OpenFolder(paths.Logs));

        RefreshOverlayMonitors();
        LoadProfile(activeProfile);
        _ = InitializeAsync();
    }

    public ObservableCollection<NavigationItemViewModel> Navigation { get; }
    public ObservableCollection<UserProfile> Profiles { get; }
    public ObservableCollection<AudioSourceViewModel> AudioSources { get; }
    public ObservableCollection<AudioEndpointInfo> Endpoints { get; }
    public ObservableCollection<ProcessAudioInfo> Processes { get; }
    public ObservableCollection<CaptionLineViewModel> Captions { get; }
    public ObservableCollection<CaptionLineViewModel> OverlayCaptions { get; }
    public ObservableCollection<OverlayMonitorOption> OverlayMonitors { get; }
    public ObservableCollection<ModelViewModel> Models { get; }
    public ObservableCollection<TranscriptSession> SavedSessions { get; }
    public ObservableCollection<string> Diagnostics { get; }

    public IReadOnlyList<AudioSourceKind> SourceKinds { get; } = Enum.GetValues<AudioSourceKind>();
    public IReadOnlyList<InferenceBackend> Backends { get; } = Enum.GetValues<InferenceBackend>();
    public IReadOnlyList<RecognitionPreset> RecognitionPresets { get; } = Enum.GetValues<RecognitionPreset>();
    public IReadOnlyList<ThemeMode> Themes { get; } = Enum.GetValues<ThemeMode>();
    public IReadOnlyList<OverlayCaptureMode> CaptureModes { get; } = Enum.GetValues<OverlayCaptureMode>();
    public IReadOnlyList<OverlayAnchorOption> OverlayAnchorOptions { get; } =
    [
        new(OverlayAnchor.BottomCenter, "Bottom center"),
        new(OverlayAnchor.BottomLeft, "Bottom left"),
        new(OverlayAnchor.BottomRight, "Bottom right"),
        new(OverlayAnchor.TopCenter, "Top center"),
        new(OverlayAnchor.Custom, "Custom / dragged")
    ];

    public AsyncRelayCommand ToggleListeningCommand { get; }
    public RelayCommand ToggleOverlayCommand { get; }
    public RelayCommand ToggleOverlayLockCommand { get; }
    public AsyncRelayCommand SaveSessionCommand { get; }
    public RelayCommand AddOutputSourceCommand { get; }
    public RelayCommand AddInputSourceCommand { get; }
    public RelayCommand AddProcessSourceCommand { get; }
    public RelayCommand RemoveSourceCommand { get; }
    public AsyncRelayCommand RefreshAudioCommand { get; }
    public AsyncRelayCommand TestSourcesCommand { get; }
    public AsyncRelayCommand DownloadModelCommand { get; }
    public AsyncRelayCommand VerifyModelsCommand { get; }
    public AsyncRelayCommand RunBenchmarkCommand { get; }
    public AsyncRelayCommand SaveSettingsCommand { get; }
    public AsyncRelayCommand ImportSettingsCommand { get; }
    public AsyncRelayCommand ExportSettingsCommand { get; }
    public AsyncRelayCommand SearchHistoryCommand { get; }
    public AsyncRelayCommand CopySavedSessionCommand { get; }
    public AsyncRelayCommand ExportSavedSessionCommand { get; }
    public AsyncRelayCommand DeleteSavedSessionCommand { get; }
    public AsyncRelayCommand CreateProfileCommand { get; }
    public AsyncRelayCommand DeleteProfileCommand { get; }
    public RelayCommand OpenDataFolderCommand { get; }
    public RelayCommand OpenLogsFolderCommand { get; }

    public int SelectedNavigationIndex { get => selectedNavigationIndex; set => Set(ref selectedNavigationIndex, value); }
    public bool IsListening
    {
        get => isListening;
        private set
        {
            if (!Set(ref isListening, value)) return;
            Raise(nameof(ListeningButtonText));
            Raise(nameof(CanDeleteSelectedProfile));
            Raise(nameof(CanEditSources));
            DeleteProfileCommand.RaiseCanExecuteChanged();
            RaiseSourceCommandStates();
        }
    }
    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (!Set(ref isBusy, value)) return;
            ToggleListeningCommand.RaiseCanExecuteChanged();
            TestSourcesCommand.RaiseCanExecuteChanged();
            Raise(nameof(CanEditSources));
            RaiseSourceCommandStates();
        }
    }
    public string ListeningButtonText => IsListening ? "Stop listening" : "Start listening";
    public bool CanEditSources => !IsListening && !IsBusy;
    public bool SaveThisSession { get => saveThisSession; set => Set(ref saveThisSession, value); }
    public bool OverlayVisible { get => overlayVisible; set { if (Set(ref overlayVisible, value)) OverlayVisibilityChanged?.Invoke(this, value); } }
    public string StatusText { get => statusText; private set => Set(ref statusText, value); }
    public string StatusDetail { get => statusDetail; private set => Set(ref statusDetail, value); }
    public string DuplicateRouteWarning { get => duplicateRouteWarning; private set => Set(ref duplicateRouteWarning, value); }
    public bool HasDuplicateRouteWarning => !string.IsNullOrWhiteSpace(DuplicateRouteWarning);

    public UserProfile? SelectedProfile
    {
        get => selectedProfile;
        set
        {
            if (value is null || value.Id == activeProfile.Id) { Set(ref selectedProfile, value); return; }
            var restartListening = IsListening;
            SaveProfileIntoSettings();
            activeProfile = value;
            settings = settings with { ActiveProfileId = value.Id };
            Set(ref selectedProfile, value);
            LoadProfile(value);
            _ = SaveSettingsAsync();
            if (restartListening) _ = RestartListeningForProfileAsync();
        }
    }

    public ModelViewModel? SelectedModel
    {
        get => selectedModel;
        set
        {
            if (!Set(ref selectedModel, value) || value is null) return;
            Raise(nameof(SelectedModelId));
            SaveProfileIntoSettings(value.Id);
        }
    }
    public string SelectedModelId => SelectedModel?.Id ?? activeProfile.Recognition.ModelId;
    public InferenceBackend Backend { get => backend; set => Set(ref backend, value); }
    public RecognitionPreset SelectedRecognitionPreset
    {
        get => recognitionPreset;
        set
        {
            if (!Set(ref recognitionPreset, value) || value == RecognitionPreset.Custom) return;
            ApplyRecognitionPreset(value);
        }
    }
    public int DraftIntervalMs { get => draftIntervalMs; set { if (Set(ref draftIntervalMs, value)) MarkRecognitionCustom(); } }
    public int FinalizeSilenceMs { get => finalizeSilenceMs; set { if (Set(ref finalizeSilenceMs, value)) MarkRecognitionCustom(); } }
    public int WindowOverlapMs { get => windowOverlapMs; set { if (Set(ref windowOverlapMs, value)) MarkRecognitionCustom(); } }
    public int MaxUtteranceSeconds { get => maxUtteranceSeconds; set { if (Set(ref maxUtteranceSeconds, value)) MarkRecognitionCustom(); } }
    public float VadThreshold { get => vadThreshold; set { if (Set(ref vadThreshold, value)) MarkRecognitionCustom(); } }
    public float NoiseFloor
    {
        get => noiseFloor;
        set
        {
            if (!Set(ref noiseFloor, value)) return;
            Raise(nameof(AudioSensitivity));
            MarkRecognitionCustom();
        }
    }
    public double AudioSensitivity
    {
        get => AudioSensitivityScale.FromNoiseFloor(NoiseFloor);
        set => NoiseFloor = AudioSensitivityScale.ToNoiseFloor(value);
    }
    public string CustomVocabulary { get => customVocabulary; set => Set(ref customVocabulary, value); }

    public double OverlayWidth { get => overlayWidth; set { if (Set(ref overlayWidth, value)) OverlayProfileChanged?.Invoke(this, EventArgs.Empty); } }
    public int OverlayMaxLines { get => overlayMaxLines; set { if (Set(ref overlayMaxLines, value)) { RebuildOverlayCaptions(); OverlayProfileChanged?.Invoke(this, EventArgs.Empty); } } }
    public string OverlayFontFamily { get => overlayFontFamily; set { if (Set(ref overlayFontFamily, value)) OverlayProfileChanged?.Invoke(this, EventArgs.Empty); } }
    public double OverlayFontSize { get => overlayFontSize; set { if (Set(ref overlayFontSize, value)) OverlayProfileChanged?.Invoke(this, EventArgs.Empty); } }
    public int OverlayFontWeight { get => overlayFontWeight; set { if (Set(ref overlayFontWeight, value)) OverlayProfileChanged?.Invoke(this, EventArgs.Empty); } }
    public string OverlayTextColor { get => overlayTextColor; set { if (Set(ref overlayTextColor, value)) OverlayProfileChanged?.Invoke(this, EventArgs.Empty); } }
    public string OverlayDraftColor { get => overlayDraftColor; set { if (Set(ref overlayDraftColor, value)) OverlayProfileChanged?.Invoke(this, EventArgs.Empty); } }
    public string OverlayBackgroundColor { get => overlayBackgroundColor; set { if (Set(ref overlayBackgroundColor, value)) OverlayProfileChanged?.Invoke(this, EventArgs.Empty); } }
    public string OverlayOutlineColor { get => overlayOutlineColor; set { if (Set(ref overlayOutlineColor, value)) OverlayProfileChanged?.Invoke(this, EventArgs.Empty); } }
    public double OverlayOpacity { get => overlayOpacity; set { if (Set(ref overlayOpacity, value)) OverlayProfileChanged?.Invoke(this, EventArgs.Empty); } }
    public double OverlayCornerRadius { get => overlayCornerRadius; set { if (Set(ref overlayCornerRadius, value)) OverlayProfileChanged?.Invoke(this, EventArgs.Empty); } }
    public double OverlayOutlineThickness { get => overlayOutlineThickness; set { if (Set(ref overlayOutlineThickness, value)) OverlayProfileChanged?.Invoke(this, EventArgs.Empty); } }
    public double OverlayShadowOpacity { get => overlayShadowOpacity; set { if (Set(ref overlayShadowOpacity, value)) OverlayProfileChanged?.Invoke(this, EventArgs.Empty); } }
    public double OverlayShadowBlurRadius { get => overlayShadowBlurRadius; set { if (Set(ref overlayShadowBlurRadius, value)) OverlayProfileChanged?.Invoke(this, EventArgs.Empty); } }
    public double OverlayShadowDepth { get => overlayShadowDepth; set { if (Set(ref overlayShadowDepth, value)) OverlayProfileChanged?.Invoke(this, EventArgs.Empty); } }
    public string OverlayMonitorId { get => overlayMonitorId; set { if (Set(ref overlayMonitorId, value)) OverlayProfileChanged?.Invoke(this, EventArgs.Empty); } }
    public OverlayAnchor OverlayAnchorMode { get => overlayAnchorMode; set { if (Set(ref overlayAnchorMode, value)) OverlayProfileChanged?.Invoke(this, EventArgs.Empty); } }
    public bool ShowSourceLabels { get => showSourceLabels; set { if (Set(ref showSourceLabels, value)) OverlayProfileChanged?.Invoke(this, EventArgs.Empty); } }
    public bool OverlayLocked { get => overlayLocked; set { if (Set(ref overlayLocked, value)) { Raise(nameof(OverlayLockText)); OverlayProfileChanged?.Invoke(this, EventArgs.Empty); } } }
    public string OverlayLockText => OverlayLocked ? "Unlock overlay" : "Lock overlay";
    public bool ClickThroughWhenLocked { get => clickThroughWhenLocked; set { if (Set(ref clickThroughWhenLocked, value)) OverlayProfileChanged?.Invoke(this, EventArgs.Empty); } }
    public OverlayCaptureMode OverlayCaptureMode { get => overlayCaptureMode; set { if (Set(ref overlayCaptureMode, value)) OverlayProfileChanged?.Invoke(this, EventArgs.Empty); } }

    public ThemeMode Theme { get => theme; set { if (Set(ref theme, value)) ThemeChanged?.Invoke(this, value); } }
    public bool StartWithWindows { get => startWithWindows; set => Set(ref startWithWindows, value); }
    public bool MinimizeToTray { get => minimizeToTray; set => Set(ref minimizeToTray, value); }
    public bool ReduceMotion { get => reduceMotion; set => Set(ref reduceMotion, value); }
    public string HotkeyListening { get => hotkeyListening; set => Set(ref hotkeyListening, value); }
    public string HotkeyOverlay { get => hotkeyOverlay; set => Set(ref hotkeyOverlay, value); }
    public string HotkeyLock { get => hotkeyLock; set => Set(ref hotkeyLock, value); }
    public string HotkeyNextProfile { get => hotkeyNextProfile; set => Set(ref hotkeyNextProfile, value); }
    public string HotkeySave { get => hotkeySave; set => Set(ref hotkeySave, value); }
    public string HistoryQuery { get => historyQuery; set => Set(ref historyQuery, value); }
    public TranscriptSession? SelectedSavedSession { get => selectedSavedSession; set => Set(ref selectedSavedSession, value); }
    public string BenchmarkResult { get => benchmarkResult; private set => Set(ref benchmarkResult, value); }
    public string SourceTestStatus { get => sourceTestStatus; private set => Set(ref sourceTestStatus, value); }
    public string NewProfileName
    {
        get => newProfileName;
        set
        {
            if (Set(ref newProfileName, value)) CreateProfileCommand.RaiseCanExecuteChanged();
        }
    }
    public bool CanDeleteSelectedProfile => SelectedProfile is not null
                                            && !IsListening
                                            && SelectedProfile.Id is not ("default" or "gaming" or "videos" or "calls");

    public event EventHandler<bool>? OverlayVisibilityChanged;
    public event EventHandler? OverlayProfileChanged;
    public event EventHandler<ThemeMode>? ThemeChanged;
    public event EventHandler<HotkeyApplyEventArgs>? HotkeysChanged;

    public RecognitionProfile CurrentRecognitionProfile => new()
    {
        ModelId = SelectedModelId,
        Backend = Backend,
        Preset = SelectedRecognitionPreset,
        DraftIntervalMs = DraftIntervalMs,
        FinalizeSilenceMs = FinalizeSilenceMs,
        WindowOverlapMs = WindowOverlapMs,
        MaxUtteranceSeconds = MaxUtteranceSeconds,
        VadThreshold = VadThreshold,
        NoiseFloor = NoiseFloor,
        CustomVocabulary = CustomVocabulary
    };

    public OverlayProfile CurrentOverlayProfile => new()
    {
        MonitorId = string.IsNullOrWhiteSpace(OverlayMonitorId) ? null : OverlayMonitorId,
        Anchor = OverlayAnchorMode,
        X = overlayX,
        Y = overlayY,
        Width = OverlayWidth,
        MaxLines = OverlayMaxLines,
        FontFamily = OverlayFontFamily,
        FontSize = OverlayFontSize,
        FontWeight = OverlayFontWeight,
        TextColorHex = OverlayTextColor,
        DraftColorHex = OverlayDraftColor,
        BackgroundColorHex = OverlayBackgroundColor,
        OutlineColorHex = OverlayOutlineColor,
        BackgroundOpacity = OverlayOpacity,
        CornerRadius = OverlayCornerRadius,
        OutlineThickness = OverlayOutlineThickness,
        ShadowOpacity = OverlayShadowOpacity,
        ShadowBlurRadius = OverlayShadowBlurRadius,
        ShadowDepth = OverlayShadowDepth,
        ShowSourceLabels = ShowSourceLabels,
        Locked = OverlayLocked,
        ClickThroughWhenLocked = ClickThroughWhenLocked,
        CaptureMode = OverlayCaptureMode
    };

    public HotkeyProfile CurrentHotkeyProfile => new()
    {
        ToggleListening = HotkeyListening,
        ToggleOverlay = HotkeyOverlay,
        ToggleOverlayLock = HotkeyLock,
        NextProfile = HotkeyNextProfile,
        SaveSession = HotkeySave
    };

    private async Task InitializeAsync()
    {
        await RefreshAudioAsync().ConfigureAwait(true);
        await RefreshModelsAsync().ConfigureAwait(true);
        await LoadHistoryAsync().ConfigureAwait(true);
        paths.EnsureCreated();
        Diagnostics.Add($"Windows {Environment.OSVersion.Version}");
        Diagnostics.Add($"Logical processors: {Environment.ProcessorCount}");
        Diagnostics.Add("Capture: NAudio 2.3 + isolated Windows process loopback");
        Diagnostics.Add("Recognition: Whisper.net 1.9.1, Vulkan → AVX2 CPU");
        Diagnostics.Add($"Data folder: {paths.Root}");
    }

    private void ApplyRecognitionPreset(RecognitionPreset preset)
    {
        applyingRecognitionPreset = true;
        try
        {
            (draftIntervalMs, finalizeSilenceMs, windowOverlapMs, vadThreshold, noiseFloor) = preset switch
            {
                RecognitionPreset.Balanced => (500, 650, 600, 0.50f, 0.008f),
                RecognitionPreset.Stable => (900, 900, 750, 0.55f, 0.010f),
                _ => (250, 450, 500, 0.50f, 0.008f)
            };
            Raise(nameof(DraftIntervalMs));
            Raise(nameof(FinalizeSilenceMs));
            Raise(nameof(WindowOverlapMs));
            Raise(nameof(VadThreshold));
            Raise(nameof(NoiseFloor));
            Raise(nameof(AudioSensitivity));
        }
        finally
        {
            applyingRecognitionPreset = false;
        }
    }

    private void MarkRecognitionCustom()
    {
        if (applyingRecognitionPreset || recognitionPreset == RecognitionPreset.Custom) return;
        recognitionPreset = RecognitionPreset.Custom;
        Raise(nameof(SelectedRecognitionPreset));
    }

    private async Task ToggleListeningAsync()
    {
        if (IsListening) await StopListeningAsync().ConfigureAwait(true);
        else await StartListeningAsync().ConfigureAwait(true);
    }

    public async Task StartListeningAsync()
    {
        if (IsListening || IsBusy) return;
        IsBusy = true;
        StatusText = "Preparing local recognition";
        StatusDetail = "No audio or text is sent over the network.";
        try
        {
            SaveProfileIntoSettings();
            var model = ModelCatalog.Get(SelectedModelId);
            if (!await modelDownloads.IsVerifiedAsync(model).ConfigureAwait(true))
            {
                SelectedNavigationIndex = 3;
                SelectedModel = Models.First(item => item.Id == model.Id);
                StatusText = $"Download {model.DisplayName} to start";
                StatusDetail = "Model files are fetched only when you choose Download.";
                return;
            }

            sessionId = Guid.NewGuid().ToString("N");
            engineRecoveryCount = 0;
            sessionStartedUtc = DateTimeOffset.UtcNow;
            currentSegments.Clear();
            Captions.Clear();
            OverlayCaptions.Clear();
            sessionCancellation = new CancellationTokenSource();
            engine = new EngineClient();
            engine.CaptionReceived += OnCaptionReceived;
            engine.StatusChanged += OnEngineStatus;
            engine.Faulted += OnEngineFaulted;
            var vadPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Vad", "ggml-silero-v6.2.0.bin");
            var ready = await engine.LoadModelAsync(
                sessionId,
                modelDownloads.GetInstalledPath(model),
                vadPath,
                CurrentRecognitionProfile,
                sessionCancellation.Token).ConfigureAwait(true);

            capture = new AudioCaptureCoordinator();
            capture.MeterUpdated += OnMeterUpdated;
            capture.StatusChanged += OnSourceStatusChanged;
            await capture.StartAsync(AudioSources.Select(source => source.ToProfile()), sessionCancellation.Token)
                .ConfigureAwait(true);
            audioPump = Task.Run(() => PumpAudioAsync(sessionCancellation.Token));
            IsListening = true;
            StatusText = "Listening";
            StatusDetail = $"{ready} · {AudioSources.Count(source => source.Enabled)} source(s)";
            await logger.InfoAsync("session-start", $"sources={AudioSources.Count(source => source.Enabled)} backend={Backend}")
                .ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            StatusText = "Could not start listening";
            StatusDetail = exception.Message;
            await logger.ErrorAsync("session-start-failed", exception).ConfigureAwait(true);
            await CleanRuntimeAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task StopListeningAsync()
    {
        if (!IsListening && engine is null && capture is null) return;
        IsBusy = true;
        StatusText = "Stopping safely";
        try
        {
            IsListening = false;
            await CleanRuntimeAsync().ConfigureAwait(true);
            if (SaveThisSession && currentSegments.Count > 0)
            {
                await PersistCurrentSessionAsync().ConfigureAwait(true);
                StatusText = "Session saved";
                StatusDetail = "Audio was never stored; only the final captions were saved.";
            }
            else
            {
                StatusText = "Stopped — unsaved text cleared";
                StatusDetail = "Nothing from this session was written to disk.";
            }
            Captions.Clear();
            OverlayCaptions.Clear();
            currentSegments.Clear();
            SaveThisSession = false;
            await logger.InfoAsync("session-stop", "text-cleared=true").ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RestartListeningForProfileAsync()
    {
        if (!IsListening || IsBusy) return;
        IsBusy = true;
        IsListening = false;
        StatusText = $"Switching to {activeProfile.Name}";
        StatusDetail = "Restarting local capture and recognition; the current transcript stays in memory.";
        try
        {
            await CleanRuntimeAsync().ConfigureAwait(true);
            var model = ModelCatalog.Get(SelectedModelId);
            if (!await modelDownloads.IsVerifiedAsync(model).ConfigureAwait(true))
            {
                SelectedNavigationIndex = 3;
                SelectedModel = Models.First(item => item.Id == model.Id);
                StatusText = $"Download {model.DisplayName} to use {activeProfile.Name}";
                StatusDetail = "The previous capture stopped safely; install the model, then start listening again.";
                return;
            }

            sessionCancellation = new CancellationTokenSource();
            engine = new EngineClient();
            engine.CaptionReceived += OnCaptionReceived;
            engine.StatusChanged += OnEngineStatus;
            engine.Faulted += OnEngineFaulted;
            var vadPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Vad", "ggml-silero-v6.2.0.bin");
            var ready = await engine.LoadModelAsync(
                sessionId,
                modelDownloads.GetInstalledPath(model),
                vadPath,
                CurrentRecognitionProfile,
                sessionCancellation.Token).ConfigureAwait(true);

            capture = new AudioCaptureCoordinator();
            capture.MeterUpdated += OnMeterUpdated;
            capture.StatusChanged += OnSourceStatusChanged;
            await capture.StartAsync(AudioSources.Select(source => source.ToProfile()), sessionCancellation.Token)
                .ConfigureAwait(true);
            audioPump = Task.Run(() => PumpAudioAsync(sessionCancellation.Token));
            IsListening = true;
            StatusText = $"Listening · {activeProfile.Name}";
            StatusDetail = $"{ready} · {AudioSources.Count(source => source.Enabled)} source(s)";
            await logger.InfoAsync(
                    "profile-switch",
                    $"profile={activeProfile.Id} sources={AudioSources.Count(source => source.Enabled)}")
                .ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            await CleanRuntimeAsync().ConfigureAwait(true);
            StatusText = "Profile switch stopped listening";
            StatusDetail = $"{exception.Message} Start listening to retry.";
            await logger.ErrorAsync("profile-switch-failed", exception).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
            ToggleListeningCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task CleanRuntimeAsync()
    {
        sessionCancellation?.Cancel();
        if (capture is not null)
        {
            capture.MeterUpdated -= OnMeterUpdated;
            capture.StatusChanged -= OnSourceStatusChanged;
            await capture.DisposeAsync().ConfigureAwait(true);
            capture = null;
        }
        if (audioPump is not null) { try { await audioPump.ConfigureAwait(true); } catch { } audioPump = null; }
        if (engine is not null)
        {
            engine.CaptionReceived -= OnCaptionReceived;
            engine.StatusChanged -= OnEngineStatus;
            engine.Faulted -= OnEngineFaulted;
            await engine.DisposeAsync().ConfigureAwait(true);
            engine = null;
        }
        sessionCancellation?.Dispose();
        sessionCancellation = null;
    }

    private async Task PumpAudioAsync(CancellationToken cancellationToken)
    {
        if (capture is null) return;
        try
        {
            await foreach (var chunk in capture.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                engine?.SendAudio(chunk);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void OnCaptionReceived(object? sender, CaptionEvent caption) =>
        Application.Current.Dispatcher.BeginInvoke(() => ApplyCaption(caption));

    private void ApplyCaption(CaptionEvent caption)
    {
        if (string.IsNullOrEmpty(RecognizedTextFilter.Normalize(caption.Text))) return;

        var existing = Captions.LastOrDefault(line => line.SourceId == caption.SourceId && !line.IsFinal);
        if (existing is not null)
        {
            existing.Update(caption);
        }
        else
        {
            var line = new CaptionLineViewModel(caption);
            var insertAt = Captions.TakeWhile(item => item.Start <= line.Start).Count();
            Captions.Insert(insertAt, line);
        }
        if (caption.IsFinal)
        {
            currentSegments.Add(new TranscriptSegment
            {
                SessionId = sessionId,
                SourceId = caption.SourceId,
                SourceLabel = caption.SourceLabel,
                SourceColorHex = caption.SourceColorHex,
                Start = caption.Start,
                End = caption.End,
                Text = caption.Text
            });
        }
        while (Captions.Count > 200) Captions.RemoveAt(0);
        RebuildOverlayCaptions();
        SaveSessionCommand.RaiseCanExecuteChanged();
    }

    private void RebuildOverlayCaptions()
    {
        OverlayCaptions.Clear();
        foreach (var line in Captions.TakeLast(Math.Max(1, OverlayMaxLines))) OverlayCaptions.Add(line);
    }

    private void OnMeterUpdated(object? sender, AudioChunk chunk) =>
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var source = AudioSources.FirstOrDefault(item => item.Id == chunk.SourceId);
            if (source is not null) source.Level = chunk.Level;
        });

    private void OnSourceStatusChanged(object? sender, SourceStatus status) =>
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var source = AudioSources.FirstOrDefault(item => item.Id == status.SourceId);
            if (source is null) return;
            source.State = status.State;
            source.Detail = status.Detail;
        });

    private void OnEngineStatus(object? sender, string detail) =>
        Application.Current.Dispatcher.BeginInvoke(() => StatusDetail = detail);

    private void OnEngineFaulted(object? sender, string detail)
    {
        Application.Current.Dispatcher.BeginInvoke(async () =>
        {
            await RecoverEngineAsync(detail).ConfigureAwait(true);
        });
    }

    private async Task RecoverEngineAsync(string detail)
    {
        if (!IsListening || sessionCancellation is null || sessionCancellation.IsCancellationRequested) return;
        if (!await engineRecoveryGate.WaitAsync(0).ConfigureAwait(true)) return;
        EngineClient? replacement = null;
        try
        {
            StatusText = "Recognition engine recovering";
            StatusDetail = $"{detail} Restarting with the CPU fallback…";
            var failedEngine = engine;
            engine = null;
            if (failedEngine is not null)
            {
                failedEngine.CaptionReceived -= OnCaptionReceived;
                failedEngine.StatusChanged -= OnEngineStatus;
                failedEngine.Faulted -= OnEngineFaulted;
                await failedEngine.DisposeAsync().ConfigureAwait(true);
            }

            engineRecoveryCount++;
            var model = ModelCatalog.Get(SelectedModelId);
            var fallbackProfile = CurrentRecognitionProfile with { Backend = InferenceBackend.Cpu };
            replacement = new EngineClient();
            replacement.CaptionReceived += OnCaptionReceived;
            replacement.StatusChanged += OnEngineStatus;
            replacement.Faulted += OnEngineFaulted;
            var vadPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Vad", "ggml-silero-v6.2.0.bin");
            var ready = await replacement.LoadModelAsync(
                sessionId,
                modelDownloads.GetInstalledPath(model),
                vadPath,
                fallbackProfile,
                sessionCancellation.Token).ConfigureAwait(true);
            engine = replacement;
            replacement = null;
            StatusText = "Listening";
            StatusDetail = $"Recovered after engine restart · {ready}";
            await logger.InfoAsync("engine-recovered", $"attempt={engineRecoveryCount} backend=Cpu").ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            if (replacement is not null) await replacement.DisposeAsync().ConfigureAwait(true);
            StatusText = "Recognition stopped";
            StatusDetail = $"Automatic CPU recovery failed: {exception.Message}. Stop and start listening to retry.";
            await logger.ErrorAsync("engine-recovery-failed", exception).ConfigureAwait(true);
        }
        finally
        {
            engineRecoveryGate.Release();
        }
    }

    private void AddSource(AudioSourceKind kind)
    {
        if (!CanEditSources) return;
        var number = AudioSources.Count + 1;
        var profile = new AudioSourceProfile
        {
            Kind = kind,
            Label = kind switch
            {
                AudioSourceKind.InputDevice => "Microphone",
                AudioSourceKind.Process => "Application",
                _ => "Computer audio"
            },
            ColorHex = (number % 3) switch { 0 => "#E0A35A", 1 => "#69A7FF", _ => "#72D7A0" }
        };
        var viewModel = new AudioSourceViewModel(profile);
        AudioSources.Add(viewModel);
        MatchSelections(viewModel);
        UpdateDuplicateWarning();
    }

    private void RemoveSource(AudioSourceViewModel? source)
    {
        if (source is null || IsListening) return;
        AudioSources.Remove(source);
        UpdateDuplicateWarning();
    }

    private void RaiseSourceCommandStates()
    {
        AddOutputSourceCommand.RaiseCanExecuteChanged();
        AddInputSourceCommand.RaiseCanExecuteChanged();
        AddProcessSourceCommand.RaiseCanExecuteChanged();
        RemoveSourceCommand.RaiseCanExecuteChanged();
    }

    private async Task RefreshAudioAsync()
    {
        await Task.Run(() =>
        {
            var endpoints = deviceService.EnumerateEndpoints();
            var processes = deviceService.EnumerateProcesses();
            Application.Current.Dispatcher.Invoke(() =>
            {
                Endpoints.Clear();
                foreach (var endpoint in endpoints) Endpoints.Add(endpoint);
                Processes.Clear();
                foreach (var process in processes) Processes.Add(process);
                foreach (var source in AudioSources) MatchSelections(source);
            });
        }).ConfigureAwait(true);
    }

    private async Task TestSourcesAsync()
    {
        if (IsListening || IsBusy) return;
        IsBusy = true;
        SourceTestStatus = "Testing for five seconds — play audio or speak now.";
        try
        {
            await RefreshAudioAsync().ConfigureAwait(true);
            SaveProfileIntoSettings();
            var enabled = AudioSources.Where(source => source.Enabled).Select(source => source.ToProfile()).ToList();
            if (enabled.Count == 0)
            {
                SourceTestStatus = "Enable at least one source before testing.";
                return;
            }

            sourceTestCancellation = new CancellationTokenSource();
            await using var probe = new AudioCaptureCoordinator();
            probe.MeterUpdated += OnMeterUpdated;
            probe.StatusChanged += OnSourceStatusChanged;
            try
            {
                await probe.StartAsync(enabled, sourceTestCancellation.Token).ConfigureAwait(true);
                await Task.Delay(TimeSpan.FromSeconds(5), sourceTestCancellation.Token).ConfigureAwait(true);
                SourceTestStatus = "Source test complete. Moving meters confirm audio; waiting sources show a recovery detail.";
            }
            finally
            {
                probe.MeterUpdated -= OnMeterUpdated;
                probe.StatusChanged -= OnSourceStatusChanged;
            }
        }
        catch (OperationCanceledException)
        {
            SourceTestStatus = "Source test stopped.";
        }
        catch (Exception exception)
        {
            SourceTestStatus = $"Source test failed: {exception.Message}";
            await logger.ErrorAsync("source-test-failed", exception).ConfigureAwait(true);
        }
        finally
        {
            sourceTestCancellation?.Dispose();
            sourceTestCancellation = null;
            IsBusy = false;
        }
    }

    private void MatchSelections(AudioSourceViewModel source)
    {
        if (source.Kind == AudioSourceKind.Process)
        {
            source.SelectedProcess = Processes.FirstOrDefault(process =>
                string.Equals(process.ExecutablePath, source.ProcessExecutablePath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(process.Name, source.ProcessName, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            source.SelectedEndpoint = Endpoints.FirstOrDefault(endpoint => endpoint.Kind == source.Kind
                && (endpoint.Id == source.DeviceId || source.FollowDefaultDevice && endpoint.IsDefault));
        }
    }

    private void UpdateDuplicateWarning()
    {
        DuplicateRouteWarning = AudioDeviceService.FindDuplicateRouteWarning(AudioSources.Select(source => source.ToProfile())) ?? string.Empty;
        Raise(nameof(HasDuplicateRouteWarning));
    }

    private async Task RefreshModelsAsync()
    {
        foreach (var model in Models)
        {
            model.Installed = modelDownloads.IsInstalled(model.Definition);
            model.Verified = model.Installed && await modelDownloads.IsVerifiedAsync(model.Definition).ConfigureAwait(true);
            model.State = model.Verified ? "Installed and verified" : model.Installed ? "Corrupt — download again" : "Not installed";
        }
    }

    private async Task DownloadSelectedModelAsync()
    {
        var model = SelectedModel;
        if (model is null) return;
        model.Downloading = true;
        try
        {
            var progress = new Progress<ModelDownloadProgress>(value =>
            {
                model.State = value.Stage;
                if (value.TotalBytes is > 0) model.Progress = value.DownloadedBytes * 100d / value.TotalBytes.Value;
            });
            await modelDownloads.DownloadAsync(model.Definition, progress).ConfigureAwait(true);
            model.Installed = true;
            model.Verified = true;
            model.State = "Installed and verified";
            StatusText = $"{model.DisplayName} is ready";
            StatusDetail = "The model passed SHA-256 verification.";
        }
        catch (Exception exception)
        {
            model.State = $"Download failed: {exception.Message}";
            StatusText = "Model download interrupted";
            StatusDetail = "Choose Download again to resume the partial file.";
        }
        finally
        {
            model.Downloading = false;
        }
    }

    private async Task RunBenchmarkAsync()
    {
        var model = ModelCatalog.Get(SelectedModelId);
        if (!await modelDownloads.IsVerifiedAsync(model).ConfigureAwait(true))
        {
            BenchmarkResult = "Install the selected model first";
            return;
        }

        BenchmarkResult = "Loading backend…";
        var watch = Stopwatch.StartNew();
        await using var benchmarkEngine = new EngineClient();
        try
        {
            var vadPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Vad", "ggml-silero-v6.2.0.bin");
            var ready = await benchmarkEngine.LoadModelAsync(
                "benchmark",
                modelDownloads.GetInstalledPath(model),
                vadPath,
                CurrentRecognitionProfile).ConfigureAwait(true);
            watch.Stop();
            BenchmarkResult = $"{ready} · cold start {watch.Elapsed.TotalSeconds:0.0}s";
        }
        catch (Exception exception)
        {
            BenchmarkResult = $"Benchmark failed: {exception.Message}";
        }
    }

    private async Task SaveCurrentSessionAsync()
    {
        if (currentSegments.Count == 0) return;
        SaveThisSession = true;
        await PersistCurrentSessionAsync().ConfigureAwait(true);
        StatusText = "Session saved";
        StatusDetail = "New final captions will be included again when listening stops.";
        await LoadHistoryAsync().ConfigureAwait(true);
    }

    private async Task PersistCurrentSessionAsync()
    {
        var session = new TranscriptSession
        {
            Id = sessionId,
            Name = $"Session {sessionStartedUtc.ToLocalTime():MMM d, h:mm tt}",
            StartedAtUtc = sessionStartedUtc,
            EndedAtUtc = DateTimeOffset.UtcNow,
            Saved = true,
            Segments = currentSegments.OrderBy(segment => segment.Start).ToList()
        };
        await history.SaveSessionAsync(session).ConfigureAwait(true);
    }

    private async Task LoadHistoryAsync()
    {
        var sessions = await history.SearchAsync(HistoryQuery).ConfigureAwait(true);
        SavedSessions.Clear();
        foreach (var session in sessions) SavedSessions.Add(session);
    }

    private async Task CopySavedSessionAsync()
    {
        if (SelectedSavedSession is null) return;
        var full = await history.LoadAsync(SelectedSavedSession.Id).ConfigureAwait(true);
        if (full is not null) Clipboard.SetText(SubtitleFormatter.ToPlainText(full.Segments));
    }

    private async Task ExportSavedSessionAsync()
    {
        if (SelectedSavedSession is null) return;
        var full = await history.LoadAsync(SelectedSavedSession.Id).ConfigureAwait(true);
        if (full is null) return;
        var dialog = new SaveFileDialog
        {
            FileName = $"LiveSubtitle-{full.StartedAtUtc.ToLocalTime():yyyy-MM-dd-HHmm}",
            Filter = "SubRip subtitle (*.srt)|*.srt|WebVTT subtitle (*.vtt)|*.vtt|Text transcript (*.txt)|*.txt",
            AddExtension = true
        };
        if (dialog.ShowDialog() != true) return;
        var extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
        var content = extension switch
        {
            ".vtt" => SubtitleFormatter.ToVtt(full.Segments),
            ".txt" => SubtitleFormatter.ToPlainText(full.Segments),
            _ => SubtitleFormatter.ToSrt(full.Segments)
        };
        await File.WriteAllTextAsync(dialog.FileName, content).ConfigureAwait(true);
    }

    private async Task DeleteSavedSessionAsync()
    {
        if (SelectedSavedSession is null) return;
        await history.DeleteAsync(SelectedSavedSession.Id).ConfigureAwait(true);
        SelectedSavedSession = null;
        await LoadHistoryAsync().ConfigureAwait(true);
    }

    private async Task CreateProfileAsync()
    {
        var name = NewProfileName.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;
        if (Profiles.Any(profile => string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            StatusText = "Profile name already exists";
            StatusDetail = "Choose a unique profile name.";
            return;
        }

        SaveProfileIntoSettings();
        var created = activeProfile with
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name
        };
        settings = settings with
        {
            Profiles = settings.Profiles.Append(created).ToList(),
            ActiveProfileId = created.Id
        };
        Profiles.Add(created);
        activeProfile = created;
        selectedProfile = created;
        Raise(nameof(SelectedProfile));
        Raise(nameof(CanDeleteSelectedProfile));
        DeleteProfileCommand.RaiseCanExecuteChanged();
        NewProfileName = string.Empty;
        await SaveSettingsAsync().ConfigureAwait(true);
        StatusText = $"Created {created.Name}";
        StatusDetail = "This profile is available in the control center and tray menu.";
    }

    private async Task DeleteSelectedProfileAsync()
    {
        if (!CanDeleteSelectedProfile || SelectedProfile is null) return;
        var deletedName = SelectedProfile.Name;
        var fallback = Profiles.First(profile => profile.Id == "default");
        Profiles.Remove(SelectedProfile);
        settings = settings with
        {
            Profiles = Profiles.ToList(),
            ActiveProfileId = fallback.Id
        };
        activeProfile = fallback;
        LoadProfile(fallback);
        await SaveSettingsAsync().ConfigureAwait(true);
        StatusText = $"Deleted {deletedName}";
        StatusDetail = "The Default profile is active.";
    }

    public async Task SaveSettingsAsync()
    {
        var hotkeyRequest = new HotkeyApplyEventArgs(CurrentHotkeyProfile);
        HotkeysChanged?.Invoke(this, hotkeyRequest);
        if (!string.IsNullOrWhiteSpace(hotkeyRequest.Error))
        {
            StatusText = "Shortcut conflict";
            StatusDetail = hotkeyRequest.Error;
            return;
        }

        SaveProfileIntoSettings();
        settings = settings with
        {
            General = new GeneralSettings
            {
                Theme = Theme,
                StartWithWindows = StartWithWindows,
                MinimizeToTrayWhileListening = MinimizeToTray,
                ShowOverlayOnStart = OverlayVisible,
                ShowFirstRun = settings.General.ShowFirstRun,
                ReduceMotion = ReduceMotion
            }
        };
        StartupRegistrationService.SetEnabled(StartWithWindows);
        await settingsService.SaveAsync(settings).ConfigureAwait(true);
        StatusText = "Settings saved";
        StatusDetail = "Profiles can be exported as versioned JSON.";
    }

    private async Task ImportSettingsAsync()
    {
        var dialog = new OpenFileDialog { Filter = "LiveSubtitle settings (*.json)|*.json" };
        if (dialog.ShowDialog() != true) return;
        settings = await settingsService.ImportAsync(dialog.FileName).ConfigureAwait(true);
        Profiles.Clear();
        foreach (var profile in settings.Profiles) Profiles.Add(profile);
        activeProfile = settings.Profiles.First(profile => profile.Id == settings.ActiveProfileId);
        LoadProfile(activeProfile);
        await SaveSettingsAsync().ConfigureAwait(true);
    }

    private async Task ExportSettingsAsync()
    {
        SaveProfileIntoSettings();
        var dialog = new SaveFileDialog
        {
            FileName = "LiveSubtitle-settings.json",
            Filter = "LiveSubtitle settings (*.json)|*.json",
            AddExtension = true
        };
        if (dialog.ShowDialog() == true) await settingsService.ExportAsync(settings, dialog.FileName).ConfigureAwait(true);
    }

    private void LoadProfile(UserProfile profile)
    {
        AudioSources.Clear();
        foreach (var source in profile.AudioSources) AudioSources.Add(new AudioSourceViewModel(source));
        backend = profile.Recognition.Backend;
        recognitionPreset = profile.Recognition.Preset;
        draftIntervalMs = profile.Recognition.DraftIntervalMs;
        finalizeSilenceMs = profile.Recognition.FinalizeSilenceMs;
        windowOverlapMs = profile.Recognition.WindowOverlapMs;
        maxUtteranceSeconds = profile.Recognition.MaxUtteranceSeconds;
        vadThreshold = profile.Recognition.VadThreshold;
        noiseFloor = profile.Recognition.NoiseFloor;
        customVocabulary = profile.Recognition.CustomVocabulary;
        selectedModel = Models.FirstOrDefault(model => model.Id == profile.Recognition.ModelId) ?? Models.First(model => model.Recommended);

        var overlay = profile.Overlay;
        overlayMonitorId = OverlayMonitors.Any(option => option.Id == overlay.MonitorId)
            ? overlay.MonitorId!
            : OverlayMonitors.FirstOrDefault(option => option.IsPrimary)?.Id ?? string.Empty;
        overlayAnchorMode = overlay.Anchor;
        overlayX = overlay.X;
        overlayY = overlay.Y;
        overlayWidth = overlay.Width;
        overlayMaxLines = overlay.MaxLines;
        overlayFontFamily = overlay.FontFamily;
        overlayFontSize = overlay.FontSize;
        overlayFontWeight = overlay.FontWeight;
        overlayTextColor = overlay.TextColorHex;
        overlayDraftColor = overlay.DraftColorHex;
        overlayBackgroundColor = overlay.BackgroundColorHex;
        overlayOutlineColor = overlay.OutlineColorHex;
        overlayOpacity = overlay.BackgroundOpacity;
        overlayCornerRadius = overlay.CornerRadius;
        overlayOutlineThickness = overlay.OutlineThickness;
        overlayShadowOpacity = overlay.ShadowOpacity;
        overlayShadowBlurRadius = overlay.ShadowBlurRadius;
        overlayShadowDepth = overlay.ShadowDepth;
        showSourceLabels = overlay.ShowSourceLabels;
        overlayLocked = overlay.Locked;
        clickThroughWhenLocked = overlay.ClickThroughWhenLocked;
        overlayCaptureMode = overlay.CaptureMode;

        hotkeyListening = profile.Hotkeys.ToggleListening;
        hotkeyOverlay = profile.Hotkeys.ToggleOverlay;
        hotkeyLock = profile.Hotkeys.ToggleOverlayLock;
        hotkeyNextProfile = profile.Hotkeys.NextProfile;
        hotkeySave = profile.Hotkeys.SaveSession;
        theme = settings.General.Theme;
        startWithWindows = settings.General.StartWithWindows;
        minimizeToTray = settings.General.MinimizeToTrayWhileListening;
        reduceMotion = settings.General.ReduceMotion;
        overlayVisible = settings.General.ShowOverlayOnStart;

        foreach (var property in new[]
        {
            nameof(AudioSources), nameof(Backend), nameof(SelectedRecognitionPreset), nameof(DraftIntervalMs), nameof(FinalizeSilenceMs),
            nameof(WindowOverlapMs), nameof(MaxUtteranceSeconds), nameof(VadThreshold), nameof(NoiseFloor), nameof(AudioSensitivity),
            nameof(CustomVocabulary), nameof(SelectedModel), nameof(SelectedModelId), nameof(OverlayWidth),
            nameof(OverlayMonitorId), nameof(OverlayAnchorMode),
            nameof(OverlayMaxLines), nameof(OverlayFontFamily), nameof(OverlayFontSize), nameof(OverlayFontWeight),
            nameof(OverlayTextColor), nameof(OverlayDraftColor), nameof(OverlayBackgroundColor),
            nameof(OverlayOutlineColor), nameof(OverlayOpacity), nameof(OverlayCornerRadius),
            nameof(OverlayOutlineThickness), nameof(OverlayShadowOpacity), nameof(OverlayShadowBlurRadius),
            nameof(OverlayShadowDepth), nameof(ShowSourceLabels), nameof(OverlayLocked),
            nameof(OverlayLockText), nameof(ClickThroughWhenLocked), nameof(OverlayCaptureMode), nameof(Theme),
            nameof(StartWithWindows), nameof(MinimizeToTray), nameof(ReduceMotion), nameof(OverlayVisible),
            nameof(HotkeyListening), nameof(HotkeyOverlay), nameof(HotkeyLock), nameof(HotkeyNextProfile),
            nameof(HotkeySave)
        }) Raise(property);
        selectedProfile = profile;
        Raise(nameof(SelectedProfile));
        foreach (var source in AudioSources) MatchSelections(source);
        UpdateDuplicateWarning();
        Raise(nameof(CanDeleteSelectedProfile));
        DeleteProfileCommand.RaiseCanExecuteChanged();
        OverlayProfileChanged?.Invoke(this, EventArgs.Empty);
        var hotkeyRequest = new HotkeyApplyEventArgs(CurrentHotkeyProfile);
        HotkeysChanged?.Invoke(this, hotkeyRequest);
        if (!string.IsNullOrWhiteSpace(hotkeyRequest.Error))
        {
            StatusText = "Shortcut conflict";
            StatusDetail = hotkeyRequest.Error;
        }
    }

    private void SaveProfileIntoSettings(string? modelId = null)
    {
        var updated = activeProfile with
        {
            AudioSources = AudioSources.Select(source => source.ToProfile()).ToList(),
            Recognition = CurrentRecognitionProfile with { ModelId = modelId ?? SelectedModelId },
            Overlay = CurrentOverlayProfile,
            Hotkeys = new HotkeyProfile
            {
                ToggleListening = HotkeyListening,
                ToggleOverlay = HotkeyOverlay,
                ToggleOverlayLock = HotkeyLock,
                NextProfile = HotkeyNextProfile,
                SaveSession = HotkeySave
            }
        };
        activeProfile = updated;
        var profiles = settings.Profiles.Select(profile => profile.Id == updated.Id ? updated : profile).ToList();
        settings = settings with { Profiles = profiles, ActiveProfileId = updated.Id };
        var index = Profiles.ToList().FindIndex(profile => profile.Id == updated.Id);
        if (index >= 0) Profiles[index] = updated;
        selectedProfile = updated;
        Raise(nameof(SelectedProfile));
    }

    public async Task MarkFirstRunCompleteAsync()
    {
        settings = settings with { General = settings.General with { ShowFirstRun = false } };
        await SaveSettingsAsync().ConfigureAwait(true);
    }

    public bool ShouldShowFirstRun => settings.General.ShowFirstRun;

    public void SwitchProfile(string profileId)
    {
        var profile = Profiles.FirstOrDefault(item => item.Id == profileId);
        if (profile is not null) SelectedProfile = profile;
    }

    public void CycleProfile()
    {
        if (Profiles.Count == 0) return;
        var currentIndex = SelectedProfile is null ? -1 : Profiles.IndexOf(SelectedProfile);
        SelectedProfile = Profiles[(currentIndex + 1) % Profiles.Count];
    }

    public void ReportStatus(string title, string detail)
    {
        StatusText = title;
        StatusDetail = detail;
    }

    public void RefreshOverlayMonitors()
    {
        var currentId = overlayMonitorId;
        var screens = Forms.Screen.AllScreens;
        OverlayMonitors.Clear();
        for (var index = 0; index < screens.Length; index++)
        {
            var screen = screens[index];
            var primary = screen.Primary ? " · Primary" : string.Empty;
            OverlayMonitors.Add(new OverlayMonitorOption(
                screen.DeviceName,
                $"Display {index + 1}{primary} · {screen.Bounds.Width} × {screen.Bounds.Height}",
                screen.Primary));
        }

        if (OverlayMonitors.Any(option => option.Id == currentId)) return;
        overlayMonitorId = OverlayMonitors.FirstOrDefault(option => option.IsPrimary)?.Id
                           ?? OverlayMonitors.FirstOrDefault()?.Id
                           ?? string.Empty;
        Raise(nameof(OverlayMonitorId));
        OverlayProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateOverlayPlacement(string monitorId, double x, double y, double width)
    {
        overlayMonitorId = monitorId;
        overlayAnchorMode = OverlayAnchor.Custom;
        overlayX = Math.Clamp(x, 0, 1);
        overlayY = Math.Clamp(y, 0, 1);
        overlayWidth = Math.Clamp(width, 320, 2400);
        Raise(nameof(OverlayMonitorId));
        Raise(nameof(OverlayAnchorMode));
        Raise(nameof(OverlayWidth));
    }

    private static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
    }

    public async ValueTask DisposeAsync()
    {
        sourceTestCancellation?.Cancel();
        if (IsListening || engine is not null || capture is not null) await StopListeningAsync().ConfigureAwait(true);
        await SaveSettingsAsync().ConfigureAwait(true);
    }
}
