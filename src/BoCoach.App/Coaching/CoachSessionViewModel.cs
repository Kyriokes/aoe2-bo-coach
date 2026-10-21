using System.IO;
using BoCoach.App.Overlay;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Engine;
using BoCoach.Core.Economy;
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
    private readonly string? _settingsPath;

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

    /// <summary>Si ya se pasó (o se cerró) el tutorial de primer uso (hoja de ruta C4).</summary>
    [ObservableProperty] public partial bool FirstRunDone { get; set; }

    partial void OnFirstRunDoneChanged(bool value) => SaveSettings();

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
            Stop();
            return;
        }
        if (_selected() is not { } bo)
        {
            Status = "Elegí una build order en la biblioteca para empezar el coach.";
            return;
        }
        Start(bo, _liveFrames());
    }

    public void Start(BuildOrder bo, IFrameSource frames)
    {
        Stop();
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
        _coach.Start();
        IsRunning = true;
        Status = $"Coach con \"{bo.Name}\"{(GameCiv != BoCiv ? $" como {bo.Civ}" : "")}{LanguageNote(ocr)}. Desde el juego: Ctrl+Alt+→ avanza, Ctrl+Alt+← retrocede, Ctrl+Alt+↓ saltea, Ctrl+Alt+H oculta, Ctrl+Alt+M recordatorios en el mouse.";
    }

    /// <summary>Dónde se guardan los informes de las partidas (D5).</summary>
    public string ReportsFolder { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BoCoach", "informes");

    /// <summary>Guarda el informe de una partida terminada y muestra el resumen.</summary>
    private void Finished(GameReport report)
    {
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

    /// <summary>Segundos sin reloj para bajar el overlay (la pantalla de carga se lee después: ver LiveCoach).</summary>
    public const double LoweredAfterSeconds = 0.5;

    /// <summary>Hasta qué segundo de partida se puede aceptar la BO sugerida.</summary>
    public const double SuggestionSeconds = 60;

    /// <summary>
    /// D4: la BO de la biblioteca que mejor encaja con la civ, el mapa, los rivales (el de enfrente pesa más) y la
    /// posición en el equipo, y un consejo contra el rival de enfrente.
    /// </summary>
    private void Suggest(CoachFrame frame)
    {
        if (Overlay is null || frame.Loading is not { } loading || frame.Me is not { } me || _coach is null)
            return;
        var facing = loading.Facing(me);
        var match = new MatchSetup(me.Civ, facing?.Civ, loading.Opponents(me).Where(p => p != facing).Select(p => p.Civ).ToList(),
            loading.PositionOf(me), loading.Map);
        Overlay.MatchAdvice = BuildOrderSuggester.Advice(match, _data.CivStyles) is { } advice && facing?.Civ is { } rival
            ? $"Contra {CivName(rival)} (enfrente): {advice}"
            : null;
        if (me.Civ is null)
            return;
        var library = _data.Library.List().Select(e => e.BuildOrder).OfType<BuildOrder>();
        var suggestion = BuildOrderSuggester.Suggest(library, match, _data.CivStyles);
        if (suggestion is null || suggestion.BuildOrder.Id == _coach.BuildOrder.Id)
            return;
        _suggestion = suggestion;
        Overlay.BoSuggestion = $"Sugerida: {suggestion.BuildOrder.Name} ({suggestion.Reason}). Ctrl+Alt+B para usarla";
    }

    /// <summary>El nombre de una civ en castellano (el de Latinoamérica, o el de España si el juego está así).</summary>
    private string CivName(string civ)
    {
        var names = _data.GameData.Civ(civ).Names;
        return (Language == GameLanguage.SpanishSpain && names.TryGetValue("es", out var es) ? es
            : names.TryGetValue("mx", out var mx) ? mx
            : names.TryGetValue("es", out var other) ? other
            : civ).ToLowerInvariant();
    }

    /// <summary>Ctrl+Alt+B: empieza el coach con la BO sugerida en la pantalla de carga.</summary>
    [RelayCommand]
    private void AcceptSuggestion()
    {
        if (_suggestion is not { } suggestion)
            return;
        _suggestion = null;
        Start(suggestion.BuildOrder, _liveFrames());
        Status = $"Coach con \"{suggestion.BuildOrder.Name}\", la sugerida para esta partida.";
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
        var report = _coach.Report();
        if (report.Length >= LiveCoach.MinReportSeconds)
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
        _host.SetOverlayVisible(_overlayVisible);
    }

    [RelayCommand]
    private void ToggleDebug()
    {
        if (Overlay is not null)
            Overlay.ShowDebug = !Overlay.ShowDebug;
    }

    public void Dispose() => Stop();

    private void Apply(CoachFrame frame)
    {
        if (Overlay is null)
            return;
        Overlay.Apply(frame);
        Live.Apply(frame);
        if (frame.Loading is { } loading && loading != _loading)
        {
            _loading = loading;
            Status = LoadingNote(frame);
            Suggest(frame);
        }
        // La sugerencia vale durante la carga y el primer minuto de partida.
        if ((_suggestion is not null || Overlay.MatchAdvice is not null) && frame.View.Time > SuggestionSeconds)
            (_suggestion, Overlay.BoSuggestion, Overlay.MatchAdvice) = (null, null, null);
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
