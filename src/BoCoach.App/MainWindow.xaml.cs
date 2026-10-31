using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using BoCoach.App.Calibration;
using BoCoach.App.Capture;
using BoCoach.App.Coaching;
using BoCoach.App.FirstRun;
using BoCoach.App.Hotkeys;
using BoCoach.App.Overlay;
using BoCoach.App.Web;
using BoCoach.Core.Web;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Ocr;
using BoCoach.Vision.Regions;

namespace BoCoach.App;

public partial class MainWindow : Window, IBuildOrderDialogs, ICaptureHost, ICoachHost
{
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly ScreenCapture _screen = new();
    private readonly RecordingIndicator _indicator = new();
    private GlobalHotkeys? _hotkeys;
    private HotkeyBinder? _hotkeyBinder;
    private CoachOverlay? _overlay;
    private ResourceOverlay? _resources;
    private CursorReminder? _cursor;
    private bool _calibrating;
    private readonly CoachData _data;

    // Una BO de la web pesa unos 20 KB: una respuesta de más de 8 MB no es una BO y no se carga entera en memoria.
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 8 * 1024 * 1024 };

    /// <param name="settingsPath">Dónde están los ajustes; las capturas de prueba usan unos temporales para no tocar los del usuario.</param>
    /// <param name="userDir">Dónde están la cuenta de la web (web.json) y la caché de BO; por defecto %APPDATA%\BoCoach.</param>
    public MainWindow(CoachData data, string? settingsPath = null, string? userDir = null)
    {
        InitializeComponent();
        _data = data;
        var web = new WebAccount(SharedHttp, userDir is null ? null : Path.Combine(userDir, "web.json"));
        var cache = new BuildOrderCache(Path.Combine(userDir ?? CoachData.UserDir, "cache"), () => web.Client, () => web.Settings.Token);
        BuildOrders = new BuildOrdersViewModel(cache, web, this, data.GameData);
        DataContext = BuildOrders;
        // Coach se arma después, pero el idioma se pide recién al tocar la captura de diagnóstico.
        Tools = new CaptureToolsViewModel(_screen, this, fonts: () => data.Fonts, language: () => Coach?.Language ?? GameLanguage.English,
            gameData: () => data.GameData, playerName: () => Coach?.PlayerName);
        ToolsBar.DataContext = Tools;
        // Al empezar el coach, la calibración se hace sola si el HUD del mod está en pantalla; si no, queda la manual
        // y el coach la vuelve a intentar cada tanto hasta encontrar el HUD (arrancó en el menú del juego).
        Coach = new CoachSessionViewModel(data, this, () => BuildOrders.SelectedBuildOrder, () =>
        {
            var calibrated = Tools.TryAutoCalibrate();
            return new ScreenFrames(_screen, Tools.Layout, () => Tools.TryAutoCalibrate() ? Tools.Layout : null, calibrated);
        }, settingsPath ?? CoachSettings.DefaultPath);
        // Modo desarrollador: "Grabar cada partida sola".
        var autoRecorder = new AutoRecorder(Tools, () => DevMode.Instance is { IsOn: true, AutoRecord: true });
        // Solo con la pantalla en vivo: con --replay grabaría el escritorio.
        Coach.FrameApplied += frame => autoRecorder.OnFrame(frame.InGame && Coach.IsLive);
        Coach.Stopped += autoRecorder.OnCoachStopped;
        CoachButton.DataContext = Coach;
        PlayerNameBox.DataContext = Coach;
        GameCivBox.DataContext = Coach;
        GameLanguageBox.DataContext = Coach;
        MaxVillagersBox.DataContext = Coach;
        CursorRemindersBox.DataContext = Coach;
        AutoStartBox.DataContext = Coach;
        LivePanel.DataContext = Coach;
        ModeBox.DataContext = Coach;
        Coach.Picker = new BuildOrderPicker(cache, data.CivStyles);
        Coach.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CoachSessionViewModel.Status))
                BuildOrders.Status = Coach.Status;
        };
        // La lista se pide al abrir la app (sin internet, la guardada) y se marca la última usada.
        Loaded += async (_, _) =>
        {
            BuildOrders.Select(Coach.LastBuildOrderId);
            await BuildOrders.RefreshAsync();
            BuildOrders.Select(Coach.LastBuildOrderId);
        };

        _tick.Tick += (_, _) =>
        {
            Tools.Tick();
            // D6: el coach arranca y se detiene con el proceso del juego (no mientras el tutorial mira la pantalla).
            Coach.Tick(GameWindow.IsRunning() && _firstRun is null);
            if (Tools.IsRecording)
                _indicator.SetText($"● {Tools.RecordingStatus}");
        };
        _tick.Start();
        Tools.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(CaptureToolsViewModel.IsRecording))
                return;
            if (Tools.IsRecording)
                _indicator.ShowOver(_screen.CaptureArea, "● Grabando");
            else
                _indicator.Hide();
        };
        SourceInitialized += (_, _) => RegisterHotkeys();
        Closed += (_, _) =>
        {
            _tick.Stop();
            _hotkeys?.Dispose();
            Coach.Dispose();
            Tools.Dispose();
            _indicator.Close();
        };
    }

    public BuildOrdersViewModel BuildOrders { get; }
    public CaptureToolsViewModel Tools { get; }
    public CoachSessionViewModel Coach { get; }

    // ---- Tutorial de primer uso (C4) ----

    private FirstRunWindow? _firstRun;

    /// <summary>La primera vez (sin el tutorial hecho ni "Tu nombre"), la app lo abre sola.</summary>
    public void ShowFirstRunIfNeeded()
    {
        if (!Coach.FirstRunDone && Coach.PlayerName.Trim().Length == 0)
            ShowFirstRun();
    }

    /// <summary>Abre el tutorial (o lo trae al frente). Al cerrarlo queda hecho: se vuelve a abrir con "Primeros pasos".</summary>
    public void ShowFirstRun()
    {
        if (_firstRun is not null)
        {
            _firstRun.Activate();
            return;
        }
        var tutorial = new FirstRunViewModel(_screen, _data.GameData, () => Coach.Language, Tools.TryAutoCalibrate,
            name => Coach.PlayerName = name, Coach.PlayerName);
        _firstRun = new FirstRunWindow(tutorial, Coach, Tools.InstallModCommand) { Owner = this };
        _firstRun.Closed += (_, _) =>
        {
            _firstRun = null;
            Coach.FirstRunDone = true;
        };
        _firstRun.Show();
    }

    private void FirstRun_Click(object sender, RoutedEventArgs e) => ShowFirstRun();

    private HotkeysWindow? _hotkeysWindow;

    private readonly VersionClicks _versionClicks = new();

    /// <summary>5 clics sobre la versión prenden o apagan el modo desarrollador (spec 2026-10-30, parte 2).</summary>
    private void Version_Click(object sender, MouseButtonEventArgs e)
    {
        if (!_versionClicks.Click(DateTime.Now))
            return;
        DevMode.Instance.IsOn = !DevMode.Instance.IsOn;
        BuildOrders.Status = DevMode.Instance.IsOn ? "Modo desarrollador prendido." : "Modo desarrollador apagado.";
    }

    private void Hotkeys_Click(object sender, RoutedEventArgs e) => ShowHotkeys();

    /// <summary>La ventana "Atajos" (una sola a la vez, no modal).</summary>
    private void ShowHotkeys()
    {
        if (_hotkeyBinder is null)
            return;
        if (_hotkeysWindow is not null)
        {
            _hotkeysWindow.Activate();
            return;
        }
        _hotkeysWindow = new HotkeysWindow(new HotkeysViewModel(_hotkeyBinder, HotkeyMap.Current, includeDeveloper: DevMode.Instance.IsOn)) { Owner = this };
        _hotkeysWindow.Closed += (_, _) => _hotkeysWindow = null;
        _hotkeysWindow.Show();
    }

    /// <summary>
    /// Atajos que andan con el foco en el juego (de fábrica todos con Ctrl+Alt, que el juego no usa): calibrar, grabar
    /// y manejar el coach sin salir con Alt+Tab. No le mandan nada al juego. Se cambian en la ventana "Atajos".
    /// </summary>
    private void RegisterHotkeys()
    {
        _hotkeys = new GlobalHotkeys(this);
        var actions = new Dictionary<string, Action>
        {
            ["calibrate"] = () => { if (!_calibrating) Tools.CalibrateCommand.Execute(null); },
            ["record"] = () => Tools.ToggleRecordingCommand.Execute(null),
            ["start_stop"] = () => Coach.StartStopCommand.Execute(null),
            ["advance"] = () => Coach.AdvanceCommand.Execute(null),
            ["back"] = () => Coach.BackCommand.Execute(null),
            ["skip"] = () => Coach.SkipCommand.Execute(null),
            ["pause"] = () => Coach.TogglePauseCommand.Execute(null),
            ["overlay"] = () => Coach.ToggleOverlayCommand.Execute(null),
            ["debug"] = () => Coach.ToggleDebugCommand.Execute(null),
            ["cursor_reminders"] = () => Coach.ToggleCursorRemindersCommand.Execute(null),
            ["diagnostic"] = () => Tools.DiagnosticCommand.Execute(null),
            ["accept_suggestion"] = () => Coach.AcceptSuggestionCommand.Execute(null),
        };
        var map = new HotkeyMap(Coach.Hotkeys);
        HotkeyMap.Current = map;
        HotkeyTips.Instance.Attach(map);
        map.Changed += () => Coach.SaveHotkeys(map.Overrides);
        // Los de desarrollador (calibrar, grabar, debug, diagnóstico) solo con el modo prendido.
        _hotkeyBinder = new HotkeyBinder(_hotkeys, map, actions, includeDeveloper: DevMode.Instance.IsOn);
        DevMode.Instance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(DevMode.IsOn))
                return;
            Tools.DevModeChanged();
            if (_hotkeyBinder is { } binder)
                binder.IncludeDeveloper = DevMode.Instance.IsOn;
        };
        var missing = _hotkeyBinder.RegisterAll().Select(id => map.Get(id)?.Display ?? $"{HotkeyActions.Find(id)?.Name} (sin atajo)").ToList();
        BuildOrders.Status = missing.Count > 0
            ? $"Otro programa ya usa {string.Join(", ", missing)}; usá los botones o cambialos en Atajos."
            : $"Atajos listos, también desde el juego: {map.Text("start_stop")} empieza el coach y {map.Text("advance")} da por hecho el paso (todos en Atajos).";
    }

    // ---- Overlay del coach ----

    public void ShowOverlay(OverlayViewModel overlay, PixelRect area)
    {
        CloseOverlay();
        _overlay = new CoachOverlay(overlay);
        _overlay.Show();
        _overlay.PlaceOver(area);
        _resources = new ResourceOverlay(overlay.Resources, Tools.Layout, overlay.IdleAlert);
        _resources.Show();
        _resources.PlaceOver(area);
        _cursor = new CursorReminder(overlay);
    }

    public void PlaceOverlay(PixelRect area)
    {
        _overlay?.PlaceOver(area);
        _resources?.PlaceOver(area);
    }

    public void SetOverlayVisible(bool visible)
    {
        if (_overlay is null)
            return;
        if (visible)
        {
            _overlay.Show();
            _resources?.Show();
        }
        else
        {
            _overlay.Hide();
            _resources?.Hide();
        }
        _cursor?.SetHidden(!visible);
    }

    public void CloseOverlay()
    {
        _overlay?.Close();
        _overlay = null;
        _resources?.Close();
        _resources = null;
        _cursor?.Close();
        _cursor = null;
    }

    public void RunOnUi(Action action) => Dispatcher.BeginInvoke(action);

    public void ShowReport(IReadOnlyList<string> summary, string path) =>
        new Coaching.ReportWindow(summary, () => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })) { Owner = this }.Show();

    public Core.Web.EloCandidate? ChooseElo(IReadOnlyList<Core.Web.EloCandidate> candidates)
    {
        var window = new Coaching.EloChoiceWindow(candidates) { Owner = this };
        return window.ShowDialog() == true ? window.Selected : null;
    }

    // ---- Calibración y carpetas ----

    public RegionLayout? Calibrate(RegionLayout current)
    {
        // Si se abrió desde el editor, se minimiza para que debajo de la capa transparente quede el juego.
        // Si se abrió con el atajo desde el juego, el editor ya está atrás y no se toca.
        var fromEditor = IsActive;
        var previous = WindowState;
        if (fromEditor)
            WindowState = WindowState.Minimized;
        _calibrating = true;
        try
        {
            var window = new CalibrationWindow(current, _screen, Coach.Language.CreateOcr());
            return window.ShowDialog() == true ? window.Layout : null;
        }
        finally
        {
            _calibrating = false;
            if (fromEditor)
            {
                WindowState = previous;
                Activate();
            }
        }
    }

    public void OpenFolder(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });

    public void ShowMessage(string title, string text, bool warning) =>
        MessageBox.Show(this, text, title, MessageBoxButton.OK, warning ? MessageBoxImage.Warning : MessageBoxImage.Information);

    // ---- Diálogos ----

    public bool Confirm(string message) =>
        MessageBox.Show(this, message, "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public string? AskText(string title, string message, string initial = "") => TextPrompt.Ask(this, title, message, initial);

    public WebSettings? EditWebAccount(WebSettings current)
    {
        var window = new WebAccountWindow(current) { Owner = this };
        return window.ShowDialog() == true ? window.Result : null;
    }

    private void Support_Click(object sender, RoutedEventArgs e) => OpenUrl(BuildOrders.SupportUrl);

    /// <summary>Solo direcciones http(s): las arma la web, pero no se abre cualquier cosa.</summary>
    public void OpenUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
            Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
    }
}
