using BoCoach.App.Coaching;
using BoCoach.Core;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;
using BoCoach.Vision.Regions;
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
    [ObservableProperty] public partial IReadOnlyList<string> Late { get; set; } = [];
    [ObservableProperty] public partial string? Deviation { get; set; }
    [ObservableProperty] public partial bool Behind { get; set; }
    [ObservableProperty] public partial string? Suggestion { get; set; }
    [ObservableProperty] public partial string? Alert { get; set; }
    [ObservableProperty] public partial string? Idle { get; set; }
    [ObservableProperty] public partial string? Distribution { get; set; }
    [ObservableProperty] public partial string? DistributionNow { get; set; }
    [ObservableProperty] public partial string? Warning { get; set; }
    [ObservableProperty] public partial string Debug { get; set; } = "";
    [ObservableProperty] public partial bool ShowDebug { get; set; }

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

    public void Apply(CoachFrame frame)
    {
        var view = frame.View;
        var lines = CoachText.Lines(view, data);
        Clock = view.Time > 0 ? GameClock.Format(view.Time) : "--:--";
        Progress = view.Current is { } current ? $"{current.Index + 1}/{view.TotalSteps}" : $"{view.TotalSteps}/{view.TotalSteps}";
        Current = lines.Current;
        Status = lines.Status;
        StatusReady = view.Current?.State == StepState.Ready;
        Running = lines.Running;
        Goals = lines.Goals;
        Next = lines.Next;
        Late = lines.Late.Select(l => $"Atrasado: {l}").ToList();
        Deviation = lines.Deviation;
        Behind = lines.Behind;
        Suggestion = lines.Suggestion;
        Alert = lines.Alert;
        Idle = lines.Idle;
        Distribution = lines.Distribution;
        DistributionNow = lines.DistributionNow;
        foreach (var target in Resources)
        {
            var resource = ResourceOf(target.Region);
            // Con el mod, los que hay salen del HUD; si no, de la estimación de la economía.
            target.Apply(view.Distribution?[resource], view.Actual?[resource], estimated: !view.ActualFromHud);
            if (frame.Hud?.ModBands is { } bands && bands.TryGetValue(target.Region, out var band))
                target.Band = band;
        }
        IdleAlert.Alert = frame.Hud?.IdleVillagers > 0;
        if (frame.Hud?.ModBands is { } modBands && modBands.TryGetValue(IdleAlert.Region, out var idleBand))
            IdleAlert.Band = idleBand;
        // Sin reloj (menú, pantalla de carga) no se recuerda nada: el estado puede ser viejo.
        var playing = frame is { Ended: false, Paused: false, SecondsWithoutClock: < ClockWarningSeconds };
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

    private static string? WarningFor(CoachFrame frame) => frame switch
    {
        { Ended: true } => "Terminó la grabación.",
        { Paused: true } => "En pausa: Ctrl+Alt+P para seguir.",
        { Error: { } error } => $"No se pudo leer la pantalla: {error}",
        { SecondsWithoutClock: >= ClockWarningSeconds } => "No veo el reloj del juego: ¿menú, pantalla de carga o hay que calibrar?",
        { Hud.Clipped.Count: > 0 } => $"No entra el número de {string.Join(", ", frame.Hud!.Clipped.Select(RegionNames.Label).Select(l => l.ToLowerInvariant()))}: agrandá la caja al calibrar.",
        _ => null,
    };
}
