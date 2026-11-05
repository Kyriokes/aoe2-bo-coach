using System.Globalization;
using System.IO;
using System.Net.Http;
using BoCoach.App.Overlay;
using BoCoach.Core;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;
using BoCoach.Core.Economy;
using BoCoach.Core.Web;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Ocr;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BoCoach.App.Coaching;

/// <summary>Lo que la vista hace por la sesión: la ventana del overlay y el hilo de la interfaz.</summary>
public interface ICoachHost
{
    void ShowOverlay(OverlayViewModel overlay, PixelRect area);

    /// <summary>Vuelve a ubicar el overlay (la ventana del juego se movió o cambió de tamaño).</summary>
    void PlaceOverlay(PixelRect area);

    void SetOverlayVisible(bool visible);

    void CloseOverlay();

    void RunOnUi(Action action);

    /// <summary>D5: el resumen de la partida terminada, con el informe completo guardado en <paramref name="path"/>.</summary>
    void ShowReport(IReadOnlyList<string> summary, string path);

    /// <summary>Una pregunta de sí o no (el pedido de borrado de lo compartido).</summary>
    bool Confirm(string message);

    /// <summary>D10: elegir entre los jugadores con el mismo nombre que devolvió "Buscar"; null si cancela.</summary>
    EloCandidate? ChooseElo(IReadOnlyList<EloCandidate> candidates);
}

/// <summary>Empezar y detener el coach con la BO elegida, y los atajos que lo corrigen durante la partida.</summary>
public sealed partial class CoachSessionViewModel : ObservableObject, IDisposable
{
    private readonly CoachData _data;
    private readonly ICoachHost _host;
    private readonly Func<BuildOrder?> _selected;
    private readonly Func<IFrameSource> _liveFrames;
    private LiveCoach? _coach;
    private Vision.Loading.LoadingReading? _loading;
    private SummarySuggestion? _suggestion;
    private CancellationTokenSource? _pick;
    private double _lastGameTime;
    private bool _lastInGame;
    private string? _lastSwitchBest;
    private PixelRect _placedOver;
    private bool _overlayVisible = true;
    private bool _frameAllowsOverlay;
    private bool _overlayShown;
    private readonly string? _settingsPath;
    private bool _startedAutomatically;
    private bool _autoBlocked;
    private bool _autoNoBoWarned;
    private BuildOrder? _previousBuildOrder;
    private readonly HashSet<string> _declined = [];
    private MatchupQuery? _matchupQuery;

    /// <param name="selected">La BO elegida a mano de tu lista (ya bajada).</param>
    /// <param name="liveFrames">Las capturas de la pantalla en vivo.</param>
    /// <param name="settingsPath">Dónde se guardan los ajustes (máximo de aldeanos); null para no guardarlos (tests).</param>
    /// <param name="devMode">El estado del modo desarrollador (por defecto <see cref="DevMode.Instance"/>): se carga y se guarda con los ajustes.</param>
    public CoachSessionViewModel(CoachData data, ICoachHost host, Func<BuildOrder?> selected, Func<IFrameSource> liveFrames, string? settingsPath = null,
        DevMode? devMode = null)
    {
        SelectedLane = LaneChoices[0];
        var settings = settingsPath is null ? new CoachSettings() : CoachSettings.Load(settingsPath);
        MaxVillagers = settings.MaxVillagers;
        CaptureIntervalMs = settings.CaptureIntervalMs;
        CursorReminders = settings.CursorReminders;
        HudMinimal = settings.HudMinimal;
        GameCiv = data.GameData.IsKnownCiv(settings.GameCiv) ? settings.GameCiv : BoCiv;
        AppLanguage = settings.AppLanguage;
        Language = GameLanguage.FromCode(settings.GameLanguage);
        PlayerName = settings.PlayerName;
        FirstRunDone = settings.FirstRunDone;
        AutoStart = settings.AutoStart;
        ShareGames = settings.ShareGames;
        if (settings.WasBroken)
            ShareStatus = Texts.SettingsWereBroken;
        LastBuildOrderId = settings.LastBuildOrder;
        BoMode = settings.BoMode;
        Hotkeys = settings.Hotkeys;
        _devMode = devMode ?? DevMode.Instance;
        (_devMode.IsOn, _devMode.AutoRecord) = (settings.DevMode, settings.AutoRecord);
        Elo1v1Text = settings.Elo1v1?.ToString(CultureInfo.InvariantCulture) ?? "";
        EloTeamText = settings.EloTeam?.ToString(CultureInfo.InvariantCulture) ?? "";
        ProfileId = settings.ProfileId;
        _settingsPath = settingsPath;
        _devMode.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(DevMode.IsOn) or nameof(DevMode.AutoRecord))
                SaveSettings();
        };
        _data = data;
        _host = host;
        _selected = selected;
        _liveFrames = liveFrames;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText))]
    public partial bool IsRunning { get; set; }

    [ObservableProperty] public partial string Status { get; set; } = "";

    /// <summary>Cada cuántos ms se mira la pantalla (ver <see cref="CoachSettings.CaptureIntervalMs"/>).</summary>
    [ObservableProperty] public partial int CaptureIntervalMs { get; set; }

    partial void OnCaptureIntervalMsChanged(int value) => SaveSettings();

    /// <summary>Lo que se ofrece en el modo desarrollador: el de siempre y un 20 % menos.</summary>
    public IReadOnlyList<int> CaptureIntervalOptions { get; } = [CoachSettings.DefaultCaptureIntervalMs, 400];

    /// <summary>Hasta cuántos aldeanos producen sin parar los centros urbanos (se aplica al empezar el coach).</summary>
    [ObservableProperty] public partial int MaxVillagers { get; set; }

    partial void OnMaxVillagersChanged(int value)
    {
        var clamped = CoachSettings.Clamp(value);
        if (clamped != value)
        {
            MaxVillagers = clamped;
            return;
        }
        SaveSettings();
    }

    /// <summary>
    /// Recordatorios junto al mouse: una casa cuando quedan 3 lugares de población y un centro urbano cuando está
    /// parado. Se prende y apaga desde la ventana o con Ctrl+Alt+M, y queda guardado.
    /// </summary>
    [ObservableProperty] public partial bool CursorReminders { get; set; }

    /// <summary>Instalar el diseño mínimo del mod del HUD (ver <see cref="CoachSettings.HudMinimal"/>).</summary>
    [ObservableProperty] public partial bool HudMinimal { get; set; }

    partial void OnHudMinimalChanged(bool value) => SaveSettings();

    partial void OnCursorRemindersChanged(bool value)
    {
        if (Overlay is not null)
            Overlay.CursorReminders = value;
        SaveSettings();
    }

    [RelayCommand]
    private void ToggleCursorReminders() => CursorReminders = !CursorReminders;

    /// <summary>Opción del desplegable para jugar con la civ que dice la BO.</summary>
    public static string BoCiv => Texts.GameCivFromBuildOrder;

    /// <summary>
    /// Civ con la que se juega esta partida (clave de game_data) o <see cref="BoCiv"/>. El coach usa sus
    /// bonificaciones (costos, población de las casas, recursos iniciales) en vez de las de la civ de la BO, que en las
    /// importadas es "any". Partida 20261017-163738: incas con casas de 10 de población contadas como dos.
    /// </summary>
    [ObservableProperty] public partial string GameCiv { get; set; } = BoCiv;

    partial void OnGameCivChanged(string value) => SaveSettings();

    public IReadOnlyList<string> CivOptions => [BoCiv, .. _data.GameData.Civs.Keys.Order()];

    /// <summary>
    /// Idioma en que está el juego: el log ("--Aldeano: creación finalizada--") y la edad ("Alta Edad Media") se leen
    /// con su OCR y sus frases. Se aplica al empezar el coach.
    /// </summary>
    [ObservableProperty] public partial GameLanguage Language { get; set; } = GameLanguage.English;

    /// <summary>El idioma que eligió el jugador para la app ("" = el de Windows); se aplica al abrirla.</summary>
    [ObservableProperty] public partial string AppLanguage { get; set; } = "";

    partial void OnAppLanguageChanged(string value) => SaveSettings();

    partial void OnLanguageChanged(GameLanguage value) => SaveSettings();

    public IReadOnlyList<GameLanguage> LanguageOptions => GameLanguage.All;

    /// <summary>
    /// El nombre del jugador en el juego. En la pantalla de carga el coach busca ese renglón: toma su civ (manda sobre
    /// <see cref="GameCiv"/> en esa partida) y sabe contra quién juega. Vacío, la pantalla de carga solo reinicia la
    /// partida.
    /// </summary>
    [ObservableProperty] public partial string PlayerName { get; set; } = "";

    partial void OnPlayerNameChanged(string value) => SaveSettings();

    /// <summary>D10: el Elo de 1v1 tal como se escribe; <see cref="Elo1v1"/> es el número si vale (de 1 a 4000).</summary>
    [ObservableProperty] public partial string Elo1v1Text { get; set; } = "";

    partial void OnElo1v1TextChanged(string value)
    {
        CheckElo(value);
        SaveSettings();
    }

    /// <summary>D10: el Elo de equipos tal como se escribe; <see cref="EloTeam"/> es el número si vale.</summary>
    [ObservableProperty] public partial string EloTeamText { get; set; } = "";

    partial void OnEloTeamTextChanged(string value)
    {
        CheckElo(value);
        SaveSettings();
    }

    /// <summary>Un Elo que no se entiende se avisa (si no, se descartaba sin decir nada y el coach quedaba "sin tu Elo").</summary>
    private void CheckElo(string text)
    {
        if (text.Trim().Length > 0 && ParseElo(text) is null)
            EloStatus = Texts.InvalidElo;
        else if (EloStatus == Texts.InvalidElo)
            EloStatus = "";
    }

    public int? Elo1v1 => ParseElo(Elo1v1Text);

    public int? EloTeam => ParseElo(EloTeamText);

    /// <summary>D10: el perfil del juego elegido con "Buscar".</summary>
    public long? ProfileId { get; private set; }

    /// <summary>D10: lo que pasó con "Buscar" (al lado del botón).</summary>
    [ObservableProperty] public partial string EloStatus { get; set; } = "";

    /// <summary>D10: cómo se busca un nombre en la API del juego (los tests la cambian).</summary>
    public Func<string, CancellationToken, Task<EloSearch>> SearchElo { get; set; } =
        (name, cancel) => EloLookup.SearchAsync(EloHttp, name, cancel);

    private static readonly HttpClient EloHttp = new() { MaxResponseContentBufferSize = 1 << 20 };

    /// <summary>De 1 a 4000; acepta el separador de miles ("1.200" o "1,200").</summary>
    internal static int? ParseElo(string text)
    {
        var digits = text.Trim();
        if (digits.Length is 5 or 6 && digits[^4] is '.' or ',')
            digits = digits.Remove(digits.Length - 4, 1);
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var elo) && elo is >= 1 and <= 4000 ? elo : null;
    }

    /// <summary>
    /// D10: busca "Tu nombre" en la API del juego (solo al tocar el botón) y completa los Elo que tenga el jugador. Con
    /// homónimos, el usuario elige de una lista ordenada por la última partida.
    /// </summary>
    [RelayCommand]
    private async Task SearchEloAsync()
    {
        var name = PlayerName.Trim();
        if (name.Length == 0)
        {
            EloStatus = Texts.EloNeedsName;
            return;
        }
        EloStatus = Texts.EloSearching;
        var search = await SearchElo(name, CancellationToken.None);
        if (search.Error is { } error)
        {
            EloStatus = error;
            return;
        }
        var chosen = search.Candidates.Count switch
        {
            0 => null,
            1 => search.Candidates[0],
            _ => _host.ChooseElo(search.Candidates),
        };
        if (chosen is null)
        {
            EloStatus = search.Candidates.Count == 0 ? Texts.EloNotFound(name) : "";
            return;
        }
        ProfileId = chosen.ProfileId;
        if (chosen.Elo1v1 is { } elo1v1)
            Elo1v1Text = elo1v1.ToString(CultureInfo.InvariantCulture);
        if (chosen.EloTeam is { } eloTeam)
            EloTeamText = eloTeam.ToString(CultureInfo.InvariantCulture);
        SaveSettings();
        EloStatus = Texts.EloFound(chosen.Alias, chosen.Elo1v1?.ToString(CultureInfo.InvariantCulture) ?? "-",
            chosen.EloTeam?.ToString(CultureInfo.InvariantCulture) ?? "-");
    }

    /// <summary>Si ya se pasó (o se cerró) el tutorial de primer uso (hoja de ruta C4).</summary>
    [ObservableProperty] public partial bool FirstRunDone { get; set; }

    partial void OnFirstRunDoneChanged(bool value) => SaveSettings();

    /// <summary>D6: el coach arranca solo cuando se abre el juego (con la última BO usada) y se detiene cuando se cierra.</summary>
    [ObservableProperty] public partial bool AutoStart { get; set; }

    partial void OnAutoStartChanged(bool value) => SaveSettings();

    /// <summary>"Compartir mis partidas para mejorar el coach" (spec 2026-11-02).</summary>
    [ObservableProperty] public partial bool ShareGames { get; set; } = true;

    partial void OnShareGamesChanged(bool value) => SaveSettings();

    /// <summary>La cola y el envío de los registros; null = no se comparte (las capturas de prueba).</summary>
    public RecordSharing? Sharing { get; set; }

    /// <summary>Cómo le fue al último envío, al lado del interruptor.</summary>
    [ObservableProperty] public partial string ShareStatus { get; set; } = "";

    /// <summary>
    /// Terminó el registro de una partida: si se comparte, a la cola (queda aunque se cierre la app) y a intentar mandarlo.
    /// Solo partidas reales que valgan la pena: una grabación reproducida con --replay no es una partida nueva.
    /// </summary>
    internal async Task RecordDoneAsync(GameRecordInfo info)
    {
        if (Sharing is null || !ShareGames || !IsLive || info.Length < LiveCoach.DefaultMinReportSeconds)
            return;
        try
        {
            Sharing.Enqueue(info, DateTimeOffset.Now);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShareStatus = Texts.ShareNotWritten(e.Message);
            return;
        }
        (LastRecord, _lastRecordReported) = (info, false);
        await SendPendingRecordsAsync();
    }

    /// <summary>El informe de <see cref="LastRecord"/> ya salió: el registro de una partida llega antes que su informe.</summary>
    private bool _lastRecordReported;

    /// <summary>Manda lo que esté vencido en la cola (al terminar una partida y al abrir la app, nunca jugando). Nunca tira error.</summary>
    public async Task SendPendingRecordsAsync()
    {
        if (Sharing is null || !ShareGames)
            return;
        try
        {
            var result = await Sharing.SendPendingAsync(DateTimeOffset.Now);
            if (result.Sent + result.Failed > 0)
                ShareStatus = result.Failed > 0
                    ? Texts.SharePending(Sharing.Pending)
                    : Texts.ShareSent;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or WebLibraryException)
        {
            ShareStatus = Texts.ShareFailed(e.Message);
        }
    }

    /// <summary>El registro de la última partida que se subió o quedó en la cola: la nota del informe habla de esa.</summary>
    public GameRecordInfo? LastRecord { get; private set; }

    /// <summary>Hay a dónde mandar una nota: la última partida se compartió y se sigue compartiendo.</summary>
    public bool CanSendNote => Sharing is not null && ShareGames && LastRecord is not null;

    /// <summary>
    /// La nota del informe. True si quedó guardada para salir (aunque ahora no haya conexión).
    /// </summary>
    /// <param name="game">La partida de ese informe; por defecto, la última.</param>
    public async Task<bool> SendNoteAsync(string text, GameRecordInfo? game = null)
    {
        if (!CanSendNote || string.IsNullOrWhiteSpace(text))
            return false;
        try
        {
            Sharing!.AddNote(game ?? LastRecord!, text, DateTimeOffset.Now);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShareStatus = Texts.ShareNotWritten(e.Message);
            return false;
        }
        await SendPendingRecordsAsync();
        return true;
    }

    /// <summary>El pedido de borrado de lo que subió esta instalación.</summary>
    [RelayCommand]
    private async Task ForgetSharedAsync()
    {
        if (Sharing is null || !_host.Confirm(Texts.ForgetConfirm))
            return;
        try
        {
            var forgotten = await Sharing.ForgetAsync();
            ShareStatus = Texts.Forgotten(forgotten);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or WebLibraryException)
        {
            ShareStatus = Texts.ForgetFailed(e.Message);
        }
    }

    /// <summary>Parte 4: la BO se elige sola en cada partida (la sugerida) o a mano de la lista.</summary>
    [ObservableProperty] public partial BuildOrderMode BoMode { get; set; }

    partial void OnBoModeChanged(BuildOrderMode value)
    {
        OnPropertyChanged(nameof(IsAutoMode));
        OnPropertyChanged(nameof(IsManualMode));
        // Al pasar a "Elegir de mi lista", la que está elegida manda (ver FollowSelection).
        _followedSelection = null;
        SaveSettings();
    }

    /// <summary>Para los dos botones de la ventana ("Automática" y "Elegir de tu lista").</summary>
    public bool IsAutoMode
    {
        get => BoMode == BuildOrderMode.Auto;
        set
        {
            if (value)
                BoMode = BuildOrderMode.Auto;
        }
    }

    public bool IsManualMode
    {
        get => BoMode == BuildOrderMode.Manual;
        set
        {
            if (value)
                BoMode = BuildOrderMode.Manual;
        }
    }

    /// <summary>De dónde salen las BO (la lista de la web y lo guardado). Sin asignar, ninguna: el coach corre sin BO.</summary>
    public BuildOrderPicker? Picker { get; set; }

    private BuildOrderPicker ActivePicker => Picker ??= new BuildOrderPicker(EmptyBuildOrderSource.Instance, _data.CivStyles);

    /// <summary>D6: id de la última BO con que corrió el coach (se guarda al empezar).</summary>
    public string LastBuildOrderId { get; private set; } = "";

    /// <summary>La civ que va a usar el coach con esta BO.</summary>
    private string CivFor(BuildOrder bo) => GameCiv is { Length: > 0 } civ && civ != BoCiv ? civ : bo.Civ;

    /// <summary>
    /// Sin el loop de fondo: el test da los pasos con <see cref="LiveCoach.StepAsync"/>. Con el loop, dos hilos aplicaban
    /// lecturas a la vez (en la app van todas por el hilo de la interfaz) y la suite daba mal una vez cada tanto.
    /// </summary>
    internal bool ManualSteps { get; init; }

    /// <summary>El coach en vivo mientras corre (para los tests).</summary>
    public LiveCoach? LiveCoach => _coach;

    private readonly DevMode _devMode;

    /// <summary>El atajo que eligió el jugador para una acción, como se muestra ("Ctrl+Alt+→").</summary>
    private static string Key(string action) => global::BoCoach.App.Hotkeys.HotkeyMap.Current.Text(action);

    /// <summary>Los atajos cambiados por el jugador (id → combinación), como están en settings.json.</summary>
    public IReadOnlyDictionary<string, string> Hotkeys { get; private set; }

    /// <summary>Guarda los atajos cambiados junto con los demás ajustes.</summary>
    public void SaveHotkeys(IReadOnlyDictionary<string, string> hotkeys)
    {
        Hotkeys = new Dictionary<string, string>(hotkeys);
        SaveSettings();
    }

    private void SaveSettings()
    {
        if (_settingsPath is null)
            return;
        try
        {
            new CoachSettings
            {
                MaxVillagers = MaxVillagers,
                CaptureIntervalMs = CaptureIntervalMs,
                CursorReminders = CursorReminders,
                GameCiv = GameCiv == BoCiv ? "" : GameCiv,
                AppLanguage = AppLanguage,
                GameLanguage = Language.Code,
                PlayerName = PlayerName.Trim(),
                FirstRunDone = FirstRunDone,
                AutoStart = AutoStart,
                LastBuildOrder = LastBuildOrderId,
                BoMode = BoMode,
                Elo1v1 = Elo1v1,
                EloTeam = EloTeam,
                ProfileId = ProfileId,
                Hotkeys = new(Hotkeys),
                DevMode = _devMode.IsOn,
                AutoRecord = _devMode.AutoRecord,
                HudMinimal = HudMinimal,
                ShareGames = ShareGames,
            }.Save(_settingsPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Si no se puede guardar, vale para esta sesión.
        }
    }

    public string ButtonText => IsRunning ? Texts.StopCoach : Texts.StartCoach;

    public OverlayViewModel? Overlay { get; private set; }

    /// <summary>Lo que se leyó en la última captura, para el panel "Lo que ve el coach" de la ventana.</summary>
    public LiveReadingViewModel Live { get; } = new();

    /// <summary>Cada captura procesada (en el hilo de la interfaz), por ejemplo para medir CPU y RAM.</summary>
    public event Action<CoachFrame>? FrameApplied;

    [RelayCommand]
    private void StartStop()
    {
        if (IsRunning)
        {
            // Detenido a mano con el juego abierto: no vuelve a arrancar solo hasta que el juego se cierre y se abra.
            _autoBlocked = true;
            Stop();
            return;
        }
        if ((BoMode == BuildOrderMode.Auto ? AutoBuildOrder() : _selected()) is not { } bo)
        {
            Status = Texts.ChooseToStart;
            return;
        }
        _previousBuildOrder = null;
        _declined.Clear();
        Start(bo, _liveFrames());
    }

    /// <summary>
    /// D6: la ventana lo llama cada segundo con si corre el proceso del juego. Con "Coach automático", arranca solo con la
    /// última BO usada y se detiene (con el informe) cuando el juego se cierra.
    /// Un error no sale de acá: apagaría el temporizador de la ventana para siempre (un DispatcherTimer no se vuelve a
    /// armar), y el coach dejaría de seguir al juego. Se dice en el estado y no se insiste hasta que el juego se vuelva a abrir.
    /// </summary>
    /// <param name="canStart">False mientras el tutorial mira la pantalla: no arranca, y si ya corre, sigue.</param>
    public void Tick(bool gameRunning, bool canStart = true)
    {
        try
        {
            if (!gameRunning)
            {
                _autoBlocked = false;
                if (IsRunning && _startedAutomatically)
                {
                    Stop();
                    Status = Texts.GameClosedStopped;
                }
            }
            FollowSelection();
            if (!gameRunning || !canStart || !AutoStart || IsRunning || _autoBlocked)
                return;
            if (AutoBuildOrder() is not { } bo)
            {
                if (!_autoNoBoWarned)
                {
                    _autoNoBoWarned = true;
                    Status = Texts.AutoNeedsBuildOrder;
                }
                return;
            }
            _autoNoBoWarned = false;
            _previousBuildOrder = null;
            _declined.Clear();
            // La BO tiene errores: no se insiste cada segundo.
            if (!Start(bo, _liveFrames(), automatic: true))
                _autoBlocked = true;
        }
        catch (Exception e)
        {
            _autoBlocked = true;
            Status = Texts.AutoFailed(e.Message);
        }
    }

    /// <summary>
    /// Con qué BO arranca solo. A mano, la elegida (si todavía se está bajando, la última usada); en automática, la última
    /// usada si está guardada, o la elegida y, si no hay ninguna, sin BO: la elige la pantalla de carga.
    /// </summary>
    private BuildOrder? AutoBuildOrder() => BoMode == BuildOrderMode.Manual
        ? _selected() ?? ActivePicker.Current(LastBuildOrderId)
        : ActivePicker.Current(LastBuildOrderId) ?? _selected() ?? BuildOrder.None;

    /// <summary>La elegida de la lista la última vez que se miró, para reaccionar cuando el jugador elige otra.</summary>
    private string? _followedSelection;

    /// <summary>
    /// A mano, la elegida manda: si el coach está esperando la partida con otra (arrancó solo con el juego), se reinicia con
    /// ella. En plena partida no se toca y vale para la siguiente. Lo que se aceptó con el atajo de la sugerida no se deshace:
    /// recién cuenta si el jugador elige otra en la lista.
    /// </summary>
    private void FollowSelection()
    {
        if (BoMode != BuildOrderMode.Manual || !IsRunning || !IsLive || _lastInGame || _selected() is not { } selected || selected.Id == _followedSelection)
            return;
        _followedSelection = selected.Id;
        if (_coach?.BuildOrder.Id == selected.Id)
            return;
        _previousBuildOrder = null;
        Start(selected, _liveFrames(), _startedAutomatically);
    }

    /// <param name="automatic">Arrancó solo (D6): se detiene al cerrarse el juego y cambia a la BO sugerida si puntúa más.</param>
    /// <returns>False si la BO tiene errores: el coach que corre sigue con la suya y el estado dice qué pasó.</returns>
    public bool Start(BuildOrder bo, IFrameSource frames, bool automatic = false)
    {
        // Una BO con errores no se simula: el editor y la web validan antes, pero un .json importado a mano no pasa
        // por ahí, y un count enorme agota la memoria del Planner (encola una entrada por unidad). Se valida con su
        // propia civ: jugar con otra civ una BO que pide algo que esa civ no tiene no frena al coach.
        // Sin BO no hay pasos que validar (el validador exige pasos): el coach sigue con los recordatorios.
        if (!bo.IsNone && _data.Validator.Validate(bo).Issues.FirstOrDefault(i => i.Severity == Severity.Error) is { } error)
        {
            Status = Texts.HasErrors(bo.Name, error.Message);
            return false;
        }
        // Reiniciar (otra BO en plena partida) no es detener: no avisa Stopped.
        StopCoach(notify: false);
        IsLive = frames is not RecordedFrames;
        _startedAutomatically = automatic;
        // La civ de la partida manda sobre la de la BO: el Planner, la economía y la detección de clics usan sus costos.
        bo = bo with { Civ = CivFor(bo) };
        Overlay = new OverlayViewModel(_data.GameData, bo.Name) { ShowDebug = Overlay?.ShowDebug ?? false, CursorReminders = CursorReminders };
        // El log y la edad se leen con el OCR y las frases del idioma del juego (si Windows no tiene ese OCR, el inglés).
        var ocr = Language.CreateOcr();
        var coach = _coach = new LiveCoach(bo, _data.GameData, _data.EventsFor(Language), _data.Fonts, ocr, frames,
            new EconomySettings { MaxVillagers = MaxVillagers }, PlayerName, Language.Code)
        {
            RecordsFolder = RecordsFolder,
            AppVersion = BoCoach.App.AppVersion.Text,
            KnownMaps = _data.Maps,
        };
        _loading = null;
        // Una captura que el loop termina después de detenerlo no tiene que llegar al overlay de otra sesión.
        coach.GameFinished += report => _host.RunOnUi(() => Finished(report));
        coach.RecordFinished += info => _host.RunOnUi(() => _ = RecordDoneAsync(info));
        coach.Updated += frame => _host.RunOnUi(() =>
        {
            if (_coach == coach)
                Apply(frame);
        });
        Live.Clear();
        _placedOver = frames.Area;
        _overlayVisible = true;
        _host.ShowOverlay(Overlay, _placedOver);
        // D6: el overlay se muestra recién en la pantalla de carga o con el reloj (y con el juego al frente).
        (_frameAllowsOverlay, _overlayShown) = (false, false);
        _host.SetOverlayVisible(false);
        if (bo.Id.Length > 0 && bo.Id != LastBuildOrderId)
        {
            LastBuildOrderId = bo.Id;
            SaveSettings();
        }
        if (!ManualSteps)
            _coach.Start();
        IsRunning = true;
        _followedSelection = _selected()?.Id;
        Status = Texts.CoachWaiting(bo.Name, GameCiv != BoCiv ? Texts.PlayingAs(bo.Civ) : "", LanguageNote(ocr), Key("advance"), Key("back"), Key("skip"), Key("overlay"), Key("cursor_reminders"));
        return true;
    }

    /// <summary>
    /// Dónde se guardan los informes si no se dice otra cosa. Los tests de la app lo cambian a una carpeta temporal: los
    /// que terminaban una partida dejaban informes de scouts en la carpeta del usuario (2026-10-31, los de las 17:11 y
    /// las 17:50 parecían partidas suyas).
    /// </summary>
    internal static string DefaultReportsFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BoCoach", "informes");

    /// <summary>Dónde se guardan los registros de partida si no se dice otra cosa; los tests lo cambian a una carpeta temporal.</summary>
    internal static string DefaultRecordsFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BoCoach", "registros");

    /// <summary>Dónde se guardan los registros de las partidas (spec 2026-11-02).</summary>
    public string RecordsFolder { get; init; } = DefaultRecordsFolder;

    /// <summary>Dónde se guardan los informes de las partidas (D5).</summary>
    public string ReportsFolder { get; init; } = DefaultReportsFolder;

    /// <summary>Hay una partida en curso (el coach ve el reloj): el tablero de la ventana lo muestra.</summary>
    [ObservableProperty] public partial bool InGame { get; private set; }

    /// <summary>El minuto de la partida en curso, en segundos.</summary>
    [ObservableProperty] public partial double GameTime { get; private set; }

    /// <summary>La civ de la partida en castellano, si se sabe (con "any" no hay ninguna).</summary>
    [ObservableProperty] public partial string? GameCivName { get; private set; }

    /// <summary>Terminó una partida y todavía no empezó otra, ni el jugador dijo "Listo".</summary>
    [ObservableProperty] public partial bool ReportPending { get; private set; }

    /// <summary>El jugador ya vio el informe: el tablero vuelve a lo de siempre.</summary>
    public void MarkReportSeen() => ReportPending = false;

    /// <summary>El último informe: el de esta sesión o, al abrir la app, el último de la carpeta.</summary>
    public ReportInfo? LastReport
    {
        get
        {
            if (!_reportLooked)
            {
                _reportLooked = true;
                _lastReport = LatestReportIn(ReportsFolder);
            }
            return _lastReport;
        }
        private set
        {
            (_reportLooked, _lastReport) = (true, value);
            OnPropertyChanged();
        }
    }

    private ReportInfo? _lastReport;
    private bool _reportLooked;

    /// <summary>El más nuevo por el nombre (yyyyMMdd-HHmmss.html); solo esa carpeta, sin las de adentro.</summary>
    internal static ReportInfo? LatestReportIn(string folder)
    {
        if (!Directory.Exists(folder))
            return null;
        try
        {
            return Directory.EnumerateFiles(folder, "*.html", SearchOption.TopDirectoryOnly)
                .Select(path => DateTime.TryParseExact(Path.GetFileNameWithoutExtension(path), "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? new ReportInfo(path, date) : null)
                .OfType<ReportInfo>()
                .MaxBy(r => r.Date);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Lo principal de un informe en un renglón: los clics de edad, los pasos y el centro urbano parado.</summary>
    internal static string ReportLine(GameReport report)
    {
        var parts = report.AgeClicks.Where(c => c.Click is not null).Select(c => $"{c.Age} {GameClock.Format(c.Click!.Value)}").ToList();
        if (report.Steps.Count > 0)
            parts.Add(Texts.ReportStepsDone(report.Steps.Count(s => s.Done is not null), report.Steps.Count));
        if (report.TownCenterIdle >= 1)
            parts.Add(Texts.ReportIdle(GameClock.Format(report.TownCenterIdle)));
        return string.Join(" · ", parts);
    }

    /// <summary>Guarda el informe de una partida terminada y muestra el resumen.</summary>
    internal void Finished(GameReport report)
    {
        // Esta partida no dejó registro (no se pudo escribir, se apagó el interruptor): la nota de su informe caía en la anterior.
        if (_lastRecordReported)
            LastRecord = null;
        _lastRecordReported = true;
        // D10: el cruce de la pantalla de carga de esta partida, con la duración que tuvo; se usa una sola vez.
        if (_matchupQuery is { } query)
        {
            report = report with
            {
                Matchup = MatchupText.Report(_data.Matchups(query.Civ), query, TimeSpan.FromSeconds(report.Length), CivName, MapName),
            };
            _matchupQuery = null;
        }
        try
        {
            Directory.CreateDirectory(ReportsFolder);
            var now = DateTime.Now;
            // Con el calendario común, sea cual sea el de Windows: es lo que después se ordena en LatestReportIn.
            var path = Path.Combine(ReportsFolder, $"{now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.html");
            File.WriteAllText(path, GameReportText.Html(report, now));
            LastReport = new ReportInfo(path, now, ReportLine(report), report.Length);
            ReportPending = true;
            if (!_closing)
                _host.ShowReport(GameReportText.Summary(report), path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Status = Texts.ReportNotSaved(e.Message);
        }
    }

    /// <summary>Hasta qué segundo de partida se puede aceptar la BO sugerida.</summary>
    public const double SuggestionSeconds = BuildOrderPicker.SwitchSeconds;

    /// <summary>
    /// D4: la BO de tu lista que mejor encaja con la civ, el mapa, los rivales (el de enfrente pesa más) y la
    /// posición en el equipo, y un consejo contra el rival de enfrente.
    /// </summary>
    private void Suggest(CoachFrame frame)
    {
        if (Overlay is null || frame.Loading is not { } loading)
            return;
        // Una partida nueva: lo de la anterior no vale aunque acá no se encuentre tu nombre (D10, revisión de c).
        (_matchupQuery, Overlay.MatchupLine, Overlay.MatchAdvice) = (null, null, null);
        if (frame.Me is not { } me || _coach is null)
            return;
        var facing = loading.Facing(me);
        var match = new MatchSetup(me.Civ, facing?.Civ, loading.Opponents(me).Where(p => p != facing).Select(p => p.Civ).ToList(),
            loading.PositionOf(me), loading.Map, Elo1v1);
        Overlay.MatchAdvice = BuildOrderSuggester.Advice(match, _data.CivStyles) is { } advice && facing?.Civ is { } rival
            ? Texts.AgainstFacing(CivName(rival), advice)
            : null;
        // D10: el cruce con las estadísticas reales (1v1 con el rival; por equipos, tu civ) y para el informe.
        _matchupQuery = loading.Matchup(me, Elo1v1, EloTeam);
        var matchups = me.Civ is { } myCiv ? _data.Matchups(myCiv) : MatchupStats.Empty;
        Overlay.MatchupLine = _matchupQuery is { } query ? MatchupText.Loading(matchups, query, CivName, MapName) : null;
        if (me.Civ is null)
            return;
        var picker = ActivePicker;
        var suggestion = picker.Best(match, matchups);
        // La que corre, como está guardada (sin la civ de la partida).
        var running = picker.Current(_coach.BuildOrder.Id) ?? _coach.BuildOrder;
        // Republicada en la web desde que se bajó (la lista la trae más nueva que la guardada): se baja y el coach
        // arranca con la nueva (partida 20261102-212823: jugó con la "mejorada" vieja de los aztecas).
        if (!running.IsNone && picker.InList(running.Id) && !picker.IsFresh(running.Id))
        {
            _ = RefreshRunningAsync(picker, running);
            return;
        }
        if (suggestion is not null && suggestion.Summary.Key == running.Id)
            return;
        // D6 y parte 4: en automática, cambia a la mejor de la lista cuando puntúa más que la que corre
        // (bajándola si hace falta; sin internet, la mejor guardada). Ctrl+Alt+B vuelve a la anterior.
        var runningScore = running.IsNone ? 0 : BuildOrderSuggester.Score(BuildOrderSummary.Of(running), match, _data.CivStyles, matchups);
        // Una BO de otra civ (la de la partida anterior) no sirve: cuenta como sin BO.
        var runningFits = !running.IsNone && BuildOrderPicker.Fits(running, match);
        if (BoMode == BuildOrderMode.Auto && (suggestion is null || !_declined.Contains(suggestion.Summary.Key))
            && (suggestion is null ? !runningFits && !(running.IsNone && _declined.Count > 0) : !runningFits || suggestion.Score > runningScore))
        {
            _ = SwitchForMatchAsync(picker, match, matchups, suggestion, running);
            return;
        }
        if (suggestion is null)
            return;
        _suggestion = suggestion;
        Overlay.BoSuggestion = Texts.Suggested(suggestion.Summary.Name, suggestion.Reason, Key("accept_suggestion"));
    }

    private void ForgetMatchChoices()
    {
        _declined.Clear();
        (_suggestion, _previousBuildOrder, _lastSwitchBest) = (null, null, null);
        if (Overlay is not null)
            Overlay.BoSuggestion = null;
    }

    /// <summary>Baja la versión nueva de la BO que corre y vuelve a empezar el coach con ella (antes del primer minuto).</summary>
    private async Task RefreshRunningAsync(BuildOrderPicker picker, BuildOrder running)
    {
        var session = _coach;
        BuildOrder bo;
        try
        {
            bo = await picker.GetAsync(running.Id);
        }
        catch (WebLibraryException)
        {
            return; // sin internet: sigue con la guardada
        }
        // Si no se pudo bajar, GetAsync da la guardada sin tirar error: reiniciar con la misma daba otra vuelta sin fin.
        if (!picker.IsFresh(running.Id))
            return;
        if (_coach != session || session is null || !BuildOrderPicker.CanSwitch(_lastInGame, _lastGameTime))
            return;
        if (Start(bo, _liveFrames(), _startedAutomatically))
            Status = Texts.NewVersionRunning(bo.Name);
    }

    /// <summary>
    /// Baja (si hace falta) la BO para la partida y la usa, salvo que ya haya empezado (más del primer minuto), se
    /// haya detenido o cambiado el coach, o sea la misma que corre.
    /// </summary>
    private async Task SwitchForMatchAsync(BuildOrderPicker picker, MatchSetup match, MatchupStats? matchups, SummarySuggestion? best, BuildOrder running)
    {
        _pick?.Cancel();
        var pick = _pick = new CancellationTokenSource();
        var session = _coach;
        if (best is not null && Overlay is not null)
            Overlay.BoSuggestion = Texts.DownloadingSuggested(best.Summary.Name);
        BuildOrder bo;
        try
        {
            bo = await picker.ForMatchAsync(match, matchups, pick.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (pick.IsCancellationRequested || _coach != session || session is null || bo.Id == running.Id || _declined.Contains(bo.Id)
            || (bo.IsNone && BuildOrderPicker.Fits(running, match)))
        {
            if (Overlay is not null && _coach == session)
                Overlay.BoSuggestion = null;
            return;
        }
        // Ya empezó la partida: un coach nuevo perdería lo de la pantalla de carga. Queda como sugerida con su atajo.
        if (!BuildOrderPicker.CanSwitch(_lastInGame, _lastGameTime))
        {
            if (best is not null && !bo.IsNone && Overlay is not null)
            {
                _suggestion = best;
                Overlay.BoSuggestion = Texts.Suggested(best.Summary.Name, best.Reason, Key("accept_suggestion"));
            }
            return;
        }
        if (!Start(bo, _liveFrames(), automatic: true))
        {
            // Con errores: el coach sigue con la que tiene y en esta partida no se vuelve a probar. Antes el coach moría,
            // volvía a arrancar solo, leía la pantalla de carga y se repetía todo.
            _declined.Add(bo.Id);
            if (best is not null)
                _declined.Add(best.Summary.Key);
            if (Overlay is not null)
                Overlay.BoSuggestion = null;
            return;
        }
        _previousBuildOrder = running.IsNone ? null : running;
        _lastSwitchBest = best?.Summary.Key;
        var why = best is not null && best.Summary.Key == bo.Id ? Texts.SwitchReason(best.Reason) : "";
        var back = running.IsNone ? "" : Texts.SwitchBack(Key("accept_suggestion"), running.Name);
        if (Overlay is not null)
            Overlay.BoSuggestion = Texts.Switched(bo.Name, why, back);
        Status = Texts.SuggestedRunning(bo.Name);
    }

    /// <summary>D10: el nombre de un mapa en el idioma de la app (data/maps.json), o null si no está.</summary>
    private string? MapName(string key) => _data.Maps.Get(key)?.Label;

    /// <summary>El nombre de una civ en el idioma de la app; en castellano, el de Latinoamérica, o el de España si el juego está así.</summary>
    private string CivName(string civ)
    {
        var names = _data.GameData.Civ(civ).Names;
        if (Lang.IsEnglish)
            return names.TryGetValue(Lang.English, out var en) ? en : civ;
        return (Language == GameLanguage.SpanishSpain && names.TryGetValue("es", out var es) ? es
            : names.TryGetValue("mx", out var mx) ? mx
            : names.TryGetValue("es", out var other) ? other
            : civ).ToLowerInvariant();
    }

    /// <summary>
    /// Ctrl+Alt+B: empieza el coach con la BO sugerida en la pantalla de carga; si el coach ya cambió solo a la
    /// sugerida (D6), vuelve a la anterior y no vuelve a cambiar en esta partida.
    /// </summary>
    [RelayCommand]
    private void AcceptSuggestion()
    {
        // Ya empezada la partida no hace nada: un coach nuevo tiraría todo lo que lleva (reiniciaba a los 25 minutos).
        if (!BuildOrderPicker.CanSwitchAt(_lastGameTime))
            return;
        if (_suggestion is { } suggestion)
        {
            _suggestion = null;
            _ = AcceptAsync(suggestion);
            return;
        }
        if (_previousBuildOrder is not { } previous || _coach is null)
            return;
        // No vuelve a cambiar solo en esta partida: ni a la que usó ni a la mejor de la lista (sin internet pueden ser
        // distintas: la mejor no se bajó y usó una guardada).
        _declined.Add(_coach.BuildOrder.Id);
        if (_lastSwitchBest is not null)
            _declined.Add(_lastSwitchBest);
        _previousBuildOrder = null;
        if (Start(previous, _liveFrames(), automatic: true))
            Status = Texts.BackToBuildOrder(previous.Name);
    }

    /// <summary>Baja la sugerida (si no está guardada) y empieza el coach con ella.</summary>
    private async Task AcceptAsync(SummarySuggestion suggestion)
    {
        var session = _coach;
        BuildOrder bo;
        try
        {
            bo = await ActivePicker.GetAsync(suggestion.Summary.Key);
        }
        catch (WebLibraryException e)
        {
            Status = Texts.DownloadFailed(suggestion.Summary.Name, e.Message);
            return;
        }
        if (_coach != session)
            return;
        if (Start(bo, _liveFrames(), _startedAutomatically))
            Status = Texts.SuggestedRunning(bo.Name);
    }

    /// <summary>Lo que se entendió de la pantalla de carga, para la barra de estado.</summary>
    private string LoadingNote(CoachFrame frame)
    {
        if (frame.Loading is not { } loading)
            return "";
        if (frame.Me is not { } me)
        {
            return PlayerName.Trim().Length == 0
                ? Texts.LoadingNoPlayerName(frame.Civ)
                : Texts.LoadingPlayerNotFound(PlayerName.Trim(), string.Join(", ", loading.Players.Select(p => p.Name)), frame.Civ);
        }
        var rivals = loading.Opponents(me).Select(p => p.Civ ?? "?").ToList();
        var civ = me.Civ is null ? Texts.LoadingCivUnknown(me.CivText, frame.Civ) : Texts.LoadingPlayingWith(me.Civ);
        return Texts.LoadingNote(civ, rivals.Count > 0 ? Texts.LoadingVs(string.Join(", ", rivals)) : "", loading.Map is { } map ? Texts.LoadingOnMap(map) : "");
    }

    /// <summary>Qué idioma lee, si no es el inglés, y si falta el OCR de ese idioma en este Windows.</summary>
    private string LanguageNote(OcrReader? ocr)
    {
        if (Language == GameLanguage.English)
            return "";
        var reads = ocr?.Language ?? "";
        return reads.StartsWith("es", StringComparison.OrdinalIgnoreCase)
            ? Texts.GameInLanguage(Language.ToString())
            : Texts.GameInLanguageNoOcr(Language.ToString());
    }

    public void Stop() => StopCoach(notify: true);

    private void StopCoach(bool notify)
    {
        if (_coach is null)
            return;
        // Primero el registro y después el informe: la caja de la nota pregunta por la partida de ese informe. En la
        // 20261104-114422, 3 marcas con Ctrl+Alt+F y la caja nunca apareció, porque el informe salía antes de cerrar el
        // registro; y en la segunda partida de una sesión la nota le habría tocado a la anterior.
        var report = _coach.TakePendingReport();
        if (_coach.StopAndTakeRecord() is { } record)
            _ = RecordDoneAsync(record);
        if (report is not null)
            Finished(report);
        _coach = null;
        _host.CloseOverlay();
        IsRunning = false;
        InGame = false;
        Status = Texts.CoachStopped;
        if (notify)
            Stopped?.Invoke();
    }

    /// <summary>Una opción del selector de línea de "Ya está" y "Saltear" (null = lo primero que falta).</summary>
    public sealed record LaneChoice(string Name, Lane? Lane);

    public IReadOnlyList<LaneChoice> LaneChoices { get; } =
        [new(Texts.LaneFirstMissing, null), new(Texts.LaneVillagers, Lane.Villagers), new(Texts.LaneBuild, Lane.Build), new(Texts.LaneResearch, Lane.Research), new(Texts.LaneProduce, Lane.Produce)];

    /// <summary>Sobre qué línea actúan "Ya está" y "Saltear" (botones del panel y atajos).</summary>
    [ObservableProperty] public partial LaneChoice SelectedLane { get; set; }

    [RelayCommand]
    private void Advance() => _coach?.Advance(SelectedLane.Lane);

    [RelayCommand]
    private void Back() => _coach?.Back();

    [RelayCommand]
    private void Skip() => _coach?.Skip(SelectedLane.Lane);

    /// <summary>"Algo anduvo mal": marca este momento en el registro de la partida.</summary>
    [RelayCommand]
    private void Flag() => _coach?.Flag();

    [RelayCommand]
    private void TogglePause()
    {
        if (_coach is not null)
            _coach.Paused = !_coach.Paused;
    }

    [RelayCommand]
    private void ToggleOverlay()
    {
        if (_coach is null)
            return;
        _overlayVisible = !_overlayVisible;
        UpdateOverlayShown();
    }

    /// <summary>El overlay se ve si el jugador no lo ocultó (Ctrl+Alt+H), el juego está al frente y hay partida (o pantalla de carga).</summary>
    private void UpdateOverlayShown()
    {
        var shown = _overlayVisible && _frameAllowsOverlay;
        if (shown == _overlayShown)
            return;
        _overlayShown = shown;
        _host.SetOverlayVisible(shown);
    }

    [RelayCommand]
    private void ToggleDebug()
    {
        if (Overlay is not null)
            Overlay.ShowDebug = !Overlay.ShowDebug;
    }

    public void Dispose()
    {
        _closing = true;
        Stop();
    }

    /// <summary>La app se está cerrando: el informe se guarda, pero ya no hay ventana sobre la que abrirlo.</summary>
    private bool _closing;

    /// <summary>El coach mira la pantalla de verdad (no una grabación, como con --replay).</summary>
    public bool IsLive { get; private set; }

    /// <summary>Se detuvo el coach (a mano, o solo al cerrarse el juego).</summary>
    public event Action? Stopped;

    internal void Apply(CoachFrame frame)
    {
        if (Overlay is null)
            return;
        Overlay.Apply(frame);
        Live.Apply(frame);
        _lastGameTime = frame.View.Time;
        _lastInGame = frame.InGame;
        GameTime = frame.View.Time;
        InGame = frame.InGame;
        GameCivName = frame.Civ is { Length: > 0 } civ && _data.GameData.IsKnownCiv(civ) ? CivName(civ) : null;
        // Empezó otra partida: lo de la anterior deja de ser la noticia.
        if (frame.InGame)
            ReportPending = false;
        _frameAllowsOverlay = frame is { InFront: true, Waiting: false };
        UpdateOverlayShown();
        if (frame.Loading is { } loading && loading != _loading)
        {
            // Otra pantalla de carga en el mismo coach es otra partida: lo que se rechazó o se sugirió en la anterior ya no
            // vale. Con un coach reiniciado (_loading en null) sigue siendo la misma partida.
            if (_loading is not null)
                ForgetMatchChoices();
            _loading = loading;
            ReportPending = false;
            Status = LoadingNote(frame);
            Suggest(frame);
        }
        // La sugerencia, el aviso de que cambió de BO y el atajo que vuelve a la anterior valen durante la carga y el primer
        // minuto de partida.
        if (frame.View.Time > SuggestionSeconds
            && (_suggestion is not null || _previousBuildOrder is not null || Overlay.BoSuggestion is not null || Overlay.MatchAdvice is not null || Overlay.MatchupLine is not null))
            (_suggestion, _previousBuildOrder, Overlay.BoSuggestion, Overlay.MatchAdvice, Overlay.MatchupLine) = (null, null, null, null, null);
        // El panel no se mueve nunca: siempre arriba a la derecha, también en la pantalla de carga. Abajo tapaba el
        // mapa y el minimapa cuando en plena partida se perdía el reloj; mejor que moleste en la carga que en el juego
        // (pedido del usuario, 2026-10-29).
        if (frame.Area is { IsEmpty: false } area && area != _placedOver)
        {
            _placedOver = area;
            _host.PlaceOverlay(area);
        }
        FrameApplied?.Invoke(frame);
    }
}
