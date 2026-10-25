using System.Globalization;
using System.IO;
using System.Net.Http;
using BoCoach.App.Overlay;
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

    /// <summary>
    /// Vuelve a ubicar el overlay (la ventana del juego se movió o cambió de tamaño). <paramref name="lowered"/>: sin
    /// reloj, abajo a la derecha.
    /// </summary>
    void PlaceOverlay(PixelRect area, bool lowered = false);

    void SetOverlayVisible(bool visible);

    void CloseOverlay();

    void RunOnUi(Action action);

    /// <summary>D5: el resumen de la partida terminada, con el informe completo guardado en <paramref name="path"/>.</summary>
    void ShowReport(IReadOnlyList<string> summary, string path);

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
    private bool _lowered;
    private BuildOrderSuggestion? _suggestion;
    private PixelRect _placedOver;
    private bool _overlayVisible = true;
    private bool _frameAllowsOverlay;
    private bool _overlayShown;
    private readonly string? _settingsPath;
    private bool _startedAutomatically;
    private bool _autoBlocked;
    private bool _autoNoBoWarned;
    private BuildOrder? _previousBuildOrder;
    private string? _declinedSuggestion;
    private MatchupQuery? _matchupQuery;

    /// <param name="selected">La BO elegida en la biblioteca (la versión guardada).</param>
    /// <param name="liveFrames">Las capturas de la pantalla en vivo.</param>
    /// <param name="settingsPath">Dónde se guardan los ajustes (máximo de aldeanos); null para no guardarlos (tests).</param>
    public CoachSessionViewModel(CoachData data, ICoachHost host, Func<BuildOrder?> selected, Func<IFrameSource> liveFrames, string? settingsPath = null)
    {
        var settings = settingsPath is null ? new CoachSettings() : CoachSettings.Load(settingsPath);
        MaxVillagers = settings.MaxVillagers;
        CursorReminders = settings.CursorReminders;
        GameCiv = data.GameData.IsKnownCiv(settings.GameCiv) ? settings.GameCiv : BoCiv;
        Language = GameLanguage.FromCode(settings.GameLanguage);
        PlayerName = settings.PlayerName;
        FirstRunDone = settings.FirstRunDone;
        AutoStart = settings.AutoStart;
        LastBuildOrderId = settings.LastBuildOrder;
        Elo1v1Text = settings.Elo1v1?.ToString(CultureInfo.InvariantCulture) ?? "";
        EloTeamText = settings.EloTeam?.ToString(CultureInfo.InvariantCulture) ?? "";
        ProfileId = settings.ProfileId;
        _settingsPath = settingsPath;
        _data = data;
        _host = host;
        _selected = selected;
        _liveFrames = liveFrames;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText))]
    public partial bool IsRunning { get; set; }

    [ObservableProperty] public partial string Status { get; set; } = "";

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

    partial void OnCursorRemindersChanged(bool value)
    {
        if (Overlay is not null)
            Overlay.CursorReminders = value;
        SaveSettings();
    }

    [RelayCommand]
    private void ToggleCursorReminders() => CursorReminders = !CursorReminders;

    /// <summary>Opción del desplegable para jugar con la civ que dice la BO.</summary>
    public const string BoCiv = "(la de la BO)";

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

    partial void OnElo1v1TextChanged(string value) => SaveSettings();

    /// <summary>D10: el Elo de equipos tal como se escribe; <see cref="EloTeam"/> es el número si vale.</summary>
    [ObservableProperty] public partial string EloTeamText { get; set; } = "";

    partial void OnEloTeamTextChanged(string value) => SaveSettings();

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

    private static int? ParseElo(string text) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var elo) && elo is >= 1 and <= 4000 ? elo : null;

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
            EloStatus = "Poné tu nombre primero.";
            return;
        }
        EloStatus = "Buscando...";
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
            EloStatus = search.Candidates.Count == 0 ? $"No encontré a {name} en el juego." : "";
            return;
        }
        ProfileId = chosen.ProfileId;
        if (chosen.Elo1v1 is { } elo1v1)
            Elo1v1Text = elo1v1.ToString(CultureInfo.InvariantCulture);
        if (chosen.EloTeam is { } eloTeam)
            EloTeamText = eloTeam.ToString(CultureInfo.InvariantCulture);
        SaveSettings();
        EloStatus = $"Elo de {chosen.Alias}: 1v1 {chosen.Elo1v1?.ToString(CultureInfo.InvariantCulture) ?? "-"}, "
                    + $"equipos {chosen.EloTeam?.ToString(CultureInfo.InvariantCulture) ?? "-"}.";
    }

    /// <summary>Si ya se pasó (o se cerró) el tutorial de primer uso (hoja de ruta C4).</summary>
    [ObservableProperty] public partial bool FirstRunDone { get; set; }

    partial void OnFirstRunDoneChanged(bool value) => SaveSettings();

    /// <summary>D6: el coach arranca solo cuando se abre el juego (con la última BO usada) y se detiene cuando se cierra.</summary>
    [ObservableProperty] public partial bool AutoStart { get; set; }

    partial void OnAutoStartChanged(bool value) => SaveSettings();

    /// <summary>D6: id de la última BO con que corrió el coach (se guarda al empezar).</summary>
    public string LastBuildOrderId { get; private set; } = "";

    /// <summary>La civ que va a usar el coach con esta BO.</summary>
    private string CivFor(BuildOrder bo) => GameCiv is { Length: > 0 } civ && civ != BoCiv ? civ : bo.Civ;

    /// <summary>El coach en vivo mientras corre (para los tests).</summary>
    public LiveCoach? LiveCoach => _coach;

    private void SaveSettings()
    {
        if (_settingsPath is null)
            return;
        try
        {
            new CoachSettings
            {
                MaxVillagers = MaxVillagers,
                CursorReminders = CursorReminders,
                GameCiv = GameCiv == BoCiv ? "" : GameCiv,
                GameLanguage = Language.Code,
                PlayerName = PlayerName.Trim(),
                FirstRunDone = FirstRunDone,
                AutoStart = AutoStart,
                LastBuildOrder = LastBuildOrderId,
                Elo1v1 = Elo1v1,
                EloTeam = EloTeam,
                ProfileId = ProfileId,
            }.Save(_settingsPath);
        }
        catch (IOException)
        {
            // Si no se puede guardar, vale para esta sesión.
        }
    }

    public string ButtonText => IsRunning ? "■ Detener coach" : "▶ Empezar coach";

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
        if (_selected() is not { } bo)
        {
            Status = "Elegí una build order en la biblioteca para empezar el coach.";
            return;
        }
        (_previousBuildOrder, _declinedSuggestion) = (null, null);
        Start(bo, _liveFrames());
    }

    /// <summary>
    /// D6: la ventana lo llama cada segundo con si corre el proceso del juego (y no está abierto el tutorial). Con
    /// "Coach automático", arranca solo con la última BO usada y se detiene (con el informe) cuando el juego se cierra.
    /// </summary>
    public void Tick(bool gameRunning)
    {
        if (!gameRunning)
        {
            _autoBlocked = false;
            if (IsRunning && _startedAutomatically)
            {
                Stop();
                Status = "El juego se cerró: coach detenido.";
            }
            return;
        }
        if (!AutoStart || IsRunning || _autoBlocked)
            return;
        if (AutoBuildOrder() is not { } bo)
        {
            if (!_autoNoBoWarned)
            {
                _autoNoBoWarned = true;
                Status = "Coach automático: elegí una build order en la biblioteca para que arranque solo con el juego.";
            }
            return;
        }
        _autoNoBoWarned = false;
        (_previousBuildOrder, _declinedSuggestion) = (null, null);
        Start(bo, _liveFrames(), automatic: true);
        // La BO tiene errores: no se insiste cada segundo.
        if (!IsRunning)
            _autoBlocked = true;
    }

    /// <summary>La última BO usada (si sigue en la biblioteca), o la elegida.</summary>
    private BuildOrder? AutoBuildOrder() =>
        (LastBuildOrderId.Length > 0 ? _data.Library.List().Select(e => e.BuildOrder).FirstOrDefault(b => b?.Id == LastBuildOrderId) : null)
        ?? _selected();

    /// <param name="automatic">Arrancó solo (D6): se detiene al cerrarse el juego y cambia a la BO sugerida si puntúa más.</param>
    public void Start(BuildOrder bo, IFrameSource frames, bool automatic = false)
    {
        Stop();
        _startedAutomatically = automatic;
        // Una BO con errores no se simula: el editor y la web validan antes, pero un .json importado a mano no pasa
        // por ahí, y un count enorme agota la memoria del Planner (encola una entrada por unidad). Se valida con su
        // propia civ: jugar con otra civ una BO que pide algo que esa civ no tiene no frena al coach.
        if (_data.Validator.Validate(bo).Issues.FirstOrDefault(i => i.Severity == Severity.Error) is { } error)
        {
            Status = $"\"{bo.Name}\" tiene errores ({error.Message}): corregila en el editor antes de empezar el coach.";
            return;
        }
        // La civ de la partida manda sobre la de la BO: el Planner, la economía y la detección de clics usan sus costos.
        bo = bo with { Civ = CivFor(bo) };
        Overlay = new OverlayViewModel(_data.GameData, bo.Name) { ShowDebug = Overlay?.ShowDebug ?? false, CursorReminders = CursorReminders };
        // El log y la edad se leen con el OCR y las frases del idioma del juego (si Windows no tiene ese OCR, el inglés).
        var ocr = Language.CreateOcr();
        var coach = _coach = new LiveCoach(bo, _data.GameData, _data.EventsFor(Language), _data.Fonts, ocr, frames,
            new EconomySettings { MaxVillagers = MaxVillagers }, PlayerName, Language.Code);
        _loading = null;
        // Una captura que el loop termina después de detenerlo no tiene que llegar al overlay de otra sesión.
        coach.GameFinished += report => _host.RunOnUi(() => Finished(report));
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
        _coach.Start();
        IsRunning = true;
        Status = $"Coach con \"{bo.Name}\"{(GameCiv != BoCiv ? $" como {bo.Civ}" : "")}{LanguageNote(ocr)}, esperando la partida. Desde el juego: Ctrl+Alt+→ avanza, Ctrl+Alt+← retrocede, Ctrl+Alt+↓ saltea, Ctrl+Alt+H oculta, Ctrl+Alt+M recordatorios en el mouse.";
    }

    /// <summary>Dónde se guardan los informes de las partidas (D5).</summary>
    public string ReportsFolder { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BoCoach", "informes");

    /// <summary>Guarda el informe de una partida terminada y muestra el resumen.</summary>
    private void Finished(GameReport report)
    {
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
            var path = Path.Combine(ReportsFolder, $"{now:yyyyMMdd-HHmmss}.html");
            File.WriteAllText(path, GameReportText.Html(report, now));
            _host.ShowReport(GameReportText.Summary(report), path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Status = $"No se pudo guardar el informe de la partida: {e.Message}";
        }
    }

    /// <summary>
    /// Segundos sin reloj para bajar el overlay (la pantalla de carga se lee antes: ver LiveCoach). Con medio segundo,
    /// una captura sin leer cada tanto lo hacía saltar de arriba a abajo (tercera prueba del HUD propio, 2026-10-25).
    /// </summary>
    public const double LoweredAfterSeconds = 2;

    /// <summary>Hasta qué segundo de partida se puede aceptar la BO sugerida.</summary>
    public const double SuggestionSeconds = 60;

    /// <summary>
    /// D4: la BO de la biblioteca que mejor encaja con la civ, el mapa, los rivales (el de enfrente pesa más) y la
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
            ? $"Contra {CivName(rival)} (enfrente): {advice}"
            : null;
        // D10: el cruce con las estadísticas reales (1v1 con el rival; por equipos, tu civ) y para el informe.
        _matchupQuery = loading.Matchup(me, Elo1v1, EloTeam);
        var matchups = me.Civ is { } myCiv ? _data.Matchups(myCiv) : MatchupStats.Empty;
        Overlay.MatchupLine = _matchupQuery is { } query ? MatchupText.Loading(matchups, query, CivName, MapName) : null;
        if (me.Civ is null)
            return;
        var library = _data.Library.List().Select(e => e.BuildOrder).OfType<BuildOrder>().ToList();
        var suggestion = BuildOrderSuggester.Suggest(library, match, _data.CivStyles, matchups);
        if (suggestion is null || suggestion.BuildOrder.Id == _coach.BuildOrder.Id)
            return;
        // D6: si arrancó solo, cambia a la sugerida cuando puntúa más que la que corre (la copia de la biblioteca, sin
        // la civ de la partida). Ctrl+Alt+B vuelve a la anterior.
        var running = library.FirstOrDefault(b => b.Id == _coach.BuildOrder.Id) ?? _coach.BuildOrder;
        if (_startedAutomatically && suggestion.BuildOrder.Id != _declinedSuggestion
            && suggestion.Score > BuildOrderSuggester.Score(running, match, _data.CivStyles, matchups))
        {
            _previousBuildOrder = running;
            Start(suggestion.BuildOrder, _liveFrames(), automatic: true);
            if (Overlay is not null)
                Overlay.BoSuggestion = $"Cambié a {suggestion.BuildOrder.Name} ({suggestion.Reason}). Ctrl+Alt+B vuelve a {running.Name}";
            Status = $"Coach con \"{suggestion.BuildOrder.Name}\", la sugerida para esta partida (Ctrl+Alt+B vuelve a \"{running.Name}\").";
            return;
        }
        _suggestion = suggestion;
        Overlay.BoSuggestion = $"Sugerida: {suggestion.BuildOrder.Name} ({suggestion.Reason}). Ctrl+Alt+B para usarla";
    }

    /// <summary>D10: el nombre de un mapa en castellano (data/maps.json), o null si no está.</summary>
    private string? MapName(string key) => _data.Maps.Get(key)?.Name;

    /// <summary>El nombre de una civ en castellano (el de Latinoamérica, o el de España si el juego está así).</summary>
    private string CivName(string civ)
    {
        var names = _data.GameData.Civ(civ).Names;
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
        if (_suggestion is { } suggestion)
        {
            _suggestion = null;
            Start(suggestion.BuildOrder, _liveFrames(), _startedAutomatically);
            Status = $"Coach con \"{suggestion.BuildOrder.Name}\", la sugerida para esta partida.";
            return;
        }
        if (_previousBuildOrder is not { } previous || _coach is null)
            return;
        _declinedSuggestion = _coach.BuildOrder.Id;
        _previousBuildOrder = null;
        Start(previous, _liveFrames(), automatic: true);
        Status = $"Volví a \"{previous.Name}\".";
    }

    /// <summary>Lo que se entendió de la pantalla de carga, para la barra de estado.</summary>
    private string LoadingNote(CoachFrame frame)
    {
        if (frame.Loading is not { } loading)
            return "";
        if (frame.Me is not { } me)
        {
            return PlayerName.Trim().Length == 0
                ? $"Pantalla de carga: partida nueva. Poné \"Tu nombre\" para que el coach tome tu civ sola (sigue con {frame.Civ})."
                : $"Pantalla de carga: no encontré a \"{PlayerName.Trim()}\" entre {string.Join(", ", loading.Players.Select(p => p.Name))}. Sigue con {frame.Civ}.";
        }
        var rivals = loading.Opponents(me).Select(p => p.Civ ?? "?").ToList();
        var civ = me.Civ is null ? $"no entendí tu civ (\"{me.CivText}\"), sigue con {frame.Civ}" : $"jugás con {me.Civ}";
        return $"Pantalla de carga: {civ}{(rivals.Count > 0 ? $" contra {string.Join(", ", rivals)}" : "")}{(loading.Map is { } map ? $" en {map}" : "")}.";
    }

    /// <summary>Qué idioma lee, si no es el inglés, y si falta el OCR de ese idioma en este Windows.</summary>
    private string LanguageNote(OcrReader? ocr)
    {
        if (Language == GameLanguage.English)
            return "";
        var reads = ocr?.Language ?? "";
        return reads.StartsWith("es", StringComparison.OrdinalIgnoreCase)
            ? $", juego en {Language.Label.ToLowerInvariant()}"
            : $", juego en {Language.Label.ToLowerInvariant()} (Windows no tiene el OCR en castellano: lee con el inglés)";
    }

    public void Stop()
    {
        if (_coach is null)
            return;
        if (_coach.TakePendingReport() is { } report)
            Finished(report);
        _coach.Stop();
        _coach = null;
        _host.CloseOverlay();
        IsRunning = false;
        Status = "Coach detenido.";
    }

    [RelayCommand]
    private void Advance() => _coach?.Advance();

    [RelayCommand]
    private void Back() => _coach?.Back();

    [RelayCommand]
    private void Skip() => _coach?.Skip();

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

    public void Dispose() => Stop();

    internal void Apply(CoachFrame frame)
    {
        if (Overlay is null)
            return;
        Overlay.Apply(frame);
        Live.Apply(frame);
        _frameAllowsOverlay = frame is { InFront: true, Waiting: false };
        UpdateOverlayShown();
        if (frame.Loading is { } loading && loading != _loading)
        {
            _loading = loading;
            Status = LoadingNote(frame);
            Suggest(frame);
        }
        // La sugerencia vale durante la carga y el primer minuto de partida.
        if ((_suggestion is not null || Overlay.MatchAdvice is not null || Overlay.MatchupLine is not null) && frame.View.Time > SuggestionSeconds)
            (_suggestion, Overlay.BoSuggestion, Overlay.MatchAdvice, Overlay.MatchupLine) = (null, null, null, null);
        // Sin reloj (menú, pantalla de carga) el overlay baja, así no tapa el mapa de la pantalla de carga.
        var lowered = frame.SecondsWithoutClock >= LoweredAfterSeconds;
        if (frame.Area is { IsEmpty: false } area && (area != _placedOver || lowered != _lowered))
        {
            (_placedOver, _lowered) = (area, lowered);
            _host.PlaceOverlay(area, lowered);
        }
        FrameApplied?.Invoke(frame);
    }
}
