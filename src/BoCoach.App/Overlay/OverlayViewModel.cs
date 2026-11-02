using BoCoach.App.Coaching;
using BoCoach.Core;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Regions;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BoCoach.App.Overlay;

/// <summary>Lo que muestra la ventana del overlay, armado a partir de cada <see cref="CoachFrame"/>.</summary>
public sealed partial class OverlayViewModel(GameData data, string buildOrderName) : ObservableObject
{
    /// <summary>Segundos sin leer el reloj a partir de los cuales se avisa (menú, carga, calibración).</summary>
    public const double ClockWarningSeconds = 4;

    public string BuildOrderName { get; } = buildOrderName;

    /// <summary>Los números debajo de cada recurso del HUD (la segunda parte del overlay).</summary>
    public IReadOnlyList<ResourceTargetViewModel> Resources { get; } =
    [
        new(RegionNames.Wood), new(RegionNames.Food), new(RegionNames.Gold), new(RegionNames.Stone),
    ];

    /// <summary>El aviso que parpadea sobre el botón de inactivos mientras haya aldeanos sin hacer nada.</summary>
    public ResourceTargetViewModel IdleAlert { get; } = new(RegionNames.IdleVillagers);

    [ObservableProperty] public partial string Clock { get; set; } = "--:--";
    [ObservableProperty] public partial string Progress { get; set; } = "";
    [ObservableProperty] public partial string Current { get; set; } = "Esperando la partida…";
    [ObservableProperty] public partial string? Status { get; set; }
    [ObservableProperty] public partial bool StatusReady { get; set; }
    [ObservableProperty] public partial IReadOnlyList<string> Running { get; set; } = [];

    /// <summary>Paso mixto: las otras metas del paso actual.</summary>
    [ObservableProperty] public partial IReadOnlyList<string> Goals { get; set; } = [];
    [ObservableProperty] public partial string? Next { get; set; }
    /// <summary>Los renglones fijos de las líneas (spec 2026-10-30). Con ellos no se muestran paso actual, después ni atrasados.</summary>
    [ObservableProperty] public partial IReadOnlyList<LaneLine> Lanes { get; set; } = [];

    /// <summary>Se muestran las líneas: la BO en curso, sin rama ni abandonada.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowStep))] public partial bool ShowLanes { get; set; }

    /// <summary>El título de paso de antes: rama, BO abandonada o terminada.</summary>
    public bool ShowStep => !ShowLanes;

    [ObservableProperty] public partial string? Deviation { get; set; }
    [ObservableProperty] public partial bool Behind { get; set; }
    [ObservableProperty] public partial string? Suggestion { get; set; }
    [ObservableProperty] public partial string? Alert { get; set; }

    /// <summary>
    /// Las cajas con que se leen las capturas: al recalibrar cambian, y la GD tiene que dibujarse con las nuevas (prueba
    /// de la v0.9.71, 2026-11-01: con la barra corrida, la GD seguía en los casilleros de la calibración del arranque).
    /// </summary>
    [ObservableProperty] public partial RegionLayout? Regions { get; set; }

    /// <summary>El aviso es un clic de edad del rival: su número (1 azul ... 8 naranja, 0 si no se sabe); null si no.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(RivalClick), nameof(AlertForeground), nameof(AlertBackground))]
    public partial int? RivalSlot { get; set; }

    /// <summary>Medio ciclo del titileo del clic del rival y del renglón crítico (lo prende y apaga <see cref="Blink"/>).</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(AlertForeground), nameof(AlertBackground), nameof(ReadyForeground), nameof(ReadyBackground),
        nameof(CriticalForeground), nameof(CriticalBackground))]
    public partial bool BlinkOn { get; set; }

    /// <summary>
    /// El renglón crítico (spec 2026-11-02, escalón 1): la casa que falta o el clic de edad que ya se puede hacer, grande
    /// arriba de las líneas. Null si no hay nada crítico.
    /// </summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CriticalForeground), nameof(CriticalBackground))]
    public partial string? Critical { get; set; }

    /// <summary>El crítico es algo que hacer ya (verde, titila); si no, una falta (rojo, fijo).</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CriticalForeground), nameof(CriticalBackground))]
    public partial bool CriticalDo { get; set; }

    /// <summary>El verde de "hacé clic" (el de siempre) y el oscuro del panel en la mitad del titileo.</summary>
    public Brush ReadyForeground => BlinkOn ? PanelBrush : ReadyBrush;

    /// <summary>El fondo de "hacé clic": verde en la mitad del titileo; si no, transparente.</summary>
    public Brush ReadyBackground => BlinkOn ? ReadyBrush : Brushes.Transparent;

    public Brush CriticalForeground => CriticalDo ? ReadyForeground : LackBrush;

    public Brush CriticalBackground => CriticalDo ? ReadyBackground : Brushes.Transparent;

    /// <summary>Con la BO terminada, la tarjeta tiene un "Hacé clic en ...": titila como el renglón crítico.</summary>
    private bool CardReady => Card.Any(c => c.Kind == CardKind.Ready);

    /// <summary>El aviso es un clic de edad del rival: más grande y titilando.</summary>
    public bool RivalClick => RivalSlot is not null;

    /// <summary>El color del texto del aviso: el de siempre, o el del rival en la mitad del titileo.</summary>
    public Brush AlertForeground => RivalClick && BlinkOn ? PlayerBrushes[RivalSlot!.Value] : AlertBrush;

    /// <summary>El fondo del aviso del rival: su color, apagado, en la mitad del titileo; si no, transparente.</summary>
    public Brush AlertBackground => RivalClick && !BlinkOn ? PlayerBackgrounds[RivalSlot!.Value] : Brushes.Transparent;

    /// <summary>Lo llama la ventana cada <see cref="BlinkInterval"/>: alterna el color del clic del rival.</summary>
    public void Blink()
    {
        if (RivalClick || (Critical is not null && CriticalDo) || CardReady)
            BlinkOn = !BlinkOn;
        else if (BlinkOn)
            BlinkOn = false;
    }

    /// <summary>Medio ciclo del titileo: rápido, molesto a propósito (pedido del usuario del 2026-10-31).</summary>
    public static readonly TimeSpan BlinkInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>El naranja de los avisos (el mismo del XAML).</summary>
    public static readonly Brush AlertBrush = Solid(0xFF, 0xF0, 0xA0, 0x60);

    /// <summary>El verde de "se puede hacer" (el mismo del XAML), el rojo de lo que falta y el fondo del panel.</summary>
    public static readonly Brush ReadyBrush = Solid(0xFF, 0x8C, 0xD1, 0x7D);
    public static readonly Brush LackBrush = Solid(0xFF, 0xF0, 0x80, 0x70);
    public static readonly Brush PanelBrush = Solid(0xFF, 0x1E, 0x1A, 0x16);

    /// <summary>Los colores de los jugadores del juego por número (0 = no se sabe: rojo).</summary>
    private static readonly Brush[] PlayerBrushes =
    [
        Solid(0xFF, 0xFF, 0x30, 0x30), Solid(0xFF, 0x40, 0x7C, 0xFF), Solid(0xFF, 0xFF, 0x30, 0x30), Solid(0xFF, 0x30, 0xD0, 0x40),
        Solid(0xFF, 0xFF, 0xE8, 0x20), Solid(0xFF, 0x30, 0xE6, 0xE6), Solid(0xFF, 0xC8, 0x40, 0xFF), Solid(0xFF, 0xC0, 0xC0, 0xC0),
        Solid(0xFF, 0xFF, 0x90, 0x20),
    ];

    private static readonly Brush[] PlayerBackgrounds = PlayerBrushes
        .Select(b => Solid(0x70, ((SolidColorBrush)b).Color.R, ((SolidColorBrush)b).Color.G, ((SolidColorBrush)b).Color.B)).ToArray();

    private static SolidColorBrush Solid(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
    [ObservableProperty] public partial string? Idle { get; set; }
    [ObservableProperty] public partial string? Distribution { get; set; }
    [ObservableProperty] public partial string? DistributionNow { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowPanel))] public partial string? Warning { get; set; }
    [ObservableProperty] public partial string Debug { get; set; } = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowPanel))] public partial bool ShowDebug { get; set; }

    /// <summary>
    /// Con la BO terminada (pasado el aviso de "BO terminada"): el panel muestra solo <see cref="Card"/> y los avisos de
    /// lectura (pedido del usuario, 2026-10-26).
    /// </summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowFull), nameof(ShowPanel))] public partial bool Compact { get; set; }

    /// <summary>Con <see cref="Compact"/>, lo que pide hacer algo (ver <see cref="CoachLines.Card"/>).</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowPanel))] public partial IReadOnlyList<CardLine> Card { get; set; } = [];

    /// <summary>El panel completo: la BO en curso (o recién terminada).</summary>
    public bool ShowFull => !Compact;

    /// <summary>Con la tarjeta vacía y sin avisos de lectura no se ve nada.</summary>
    public bool ShowPanel => !Compact || Card.Count > 0 || Warning is not null || ShowDebug;

    /// <summary>Recordatorios junto al mouse prendidos (el jugador los puede apagar).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCursorReminder))]
    public partial bool CursorReminders { get; set; } = true;

    /// <summary>Quedan 3 lugares de población o menos: casa junto al mouse.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCursorReminder))]
    public partial bool HouseReminder { get; set; }

    /// <summary>El centro urbano está parado y no se llegó al máximo de aldeanos: centro urbano junto al mouse.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCursorReminder))]
    public partial bool TownCenterReminder { get; set; }

    /// <summary>Hay aldeanos inactivos según el HUD: el botón de inactivos del juego junto al mouse.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCursorReminder))]
    public partial bool IdleReminder { get; set; }

    public bool ShowCursorReminder => CursorReminders && (HouseReminder || TownCenterReminder || IdleReminder);

    /// <summary>D4: la BO que encaja con la partida de la pantalla de carga ("Sugerida: ... <atajo> para usarla"), o null.</summary>
    [ObservableProperty] public partial string? BoSuggestion { get; set; }

    /// <summary>D4: el consejo contra el rival de enfrente, en la pantalla de carga y el primer minuto.</summary>
    [ObservableProperty] public partial string? MatchAdvice { get; set; }

    /// <summary>D10: las estadísticas reales del cruce (o de tu civ por equipos), en la pantalla de carga y el primer minuto.</summary>
    [ObservableProperty] public partial string? MatchupLine { get; set; }

    public void Apply(CoachFrame frame)
    {
        var view = frame.View;
        var lines = CoachText.Lines(view, data, Hotkeys.HotkeyMap.Current.Text("back"));
        Clock = view.Time > 0 ? GameClock.Format(view.Time) : "--:--";
        Progress = view.Abandoned is not null ? "-"
            : view.Current is { } current ? $"{current.Index + 1}/{view.TotalSteps}" : $"{view.TotalSteps}/{view.TotalSteps}";
        Current = lines.Current;
        Status = lines.Status;
        StatusReady = view.Current?.State == StepState.Ready || view.Posture?.NextAgeReady == true;
        ShowLanes = lines.LanesShown;
        Lanes = lines.Lanes;
        Running = lines.Running;
        Goals = lines.Goals;
        Next = lines.Next;
        Deviation = lines.Deviation;
        Behind = lines.Behind;
        Suggestion = lines.Suggestion;
        Alert = lines.Alert;
        if (frame.Regions is { } regions)
            Regions = regions;
        RivalSlot = lines.RivalSlot;
        Critical = lines.Critical;
        CriticalDo = lines.CriticalKind == CriticalKind.Do;
        Idle = lines.Idle;
        Distribution = lines.Distribution;
        DistributionNow = lines.DistributionNow;
        Compact = lines.Compact;
        Card = lines.Card;
        foreach (var target in Resources)
        {
            var resource = ResourceOf(target.Region);
            // Con el mod, los que hay salen del HUD; si no, de la estimación de la economía.
            target.Apply(view.Distribution?[resource], view.Actual?[resource], estimated: !view.ActualFromHud);
            if (frame.Hud?.ModBands is { } bands && bands.TryGetValue(target.Region, out var band))
            {
                target.Band = OnScreen(band, frame.HudScale);
                target.Chip = frame.Hud.LayoutName is not null;
            }
        }
        IdleAlert.Alert = frame.Hud?.IdleVillagers > 0;
        if (frame.Hud?.ModBands is { } modBands && modBands.TryGetValue(IdleAlert.Region, out var idleBand))
        {
            IdleAlert.Band = OnScreen(idleBand, frame.HudScale);
            IdleAlert.Chip = frame.Hud.LayoutName is not null;
        }
        // Sin reloj (menú, pantalla de carga) no se recuerda nada: el estado puede ser viejo.
        var playing = frame is { Ended: false, Paused: false, InFront: true, Waiting: false, SecondsWithoutClock: < ClockWarningSeconds };
        HouseReminder = playing && view.NeedsHouse;
        TownCenterReminder = playing && view.TownCenterReminder;
        IdleReminder = playing && IdleAlert.Alert;
        Warning = WarningFor(frame);
        Debug = $"{frame.Usage} · {frame.FrameMs:0} ms por captura";
    }

    private static Resource ResourceOf(string region) => region switch
    {
        RegionNames.Food => Resource.Food,
        RegionNames.Wood => Resource.Wood,
        RegionNames.Stone => Resource.Stone,
        _ => Resource.Gold,
    };

    /// <summary>Una franja medida en la captura llevada a 1080p, en píxeles de la caja en la pantalla.</summary>
    private static PixelRect OnScreen(PixelRect band, double hudScale) => CapturedImage.NeedsResample(hudScale)
        ? new PixelRect(Scaled(band.X, hudScale), Scaled(band.Y, hudScale), Scaled(band.Width, hudScale), Scaled(band.Height, hudScale))
        : band;

    private static int Scaled(int pixels, double scale) => (int)Math.Round(pixels * scale);

    private static string? WarningFor(CoachFrame frame) => frame switch
    {
        { Ended: true } => "Terminó la grabación.",
        { Paused: true } => $"En pausa: {Hotkeys.HotkeyMap.Current.Text("pause")} para seguir.",
        { Error: { } error } => $"No se pudo leer la pantalla: {error}",
        { InFront: false } => "El juego no está al frente.",
        { Waiting: true } => "Esperando una partida…",
        { SecondsWithoutClock: >= ClockWarningSeconds } => "No veo el reloj del juego: ¿menú, pantalla de carga o hay que calibrar?",
        { Notice: { } notice } => notice,
        { Hud.Clipped.Count: > 0 } => $"No entra el número de {string.Join(", ", frame.Hud!.Clipped.Select(RegionNames.Label).Select(l => l.ToLowerInvariant()))}: agrandá la caja al calibrar.",
        _ => null,
    };
}
