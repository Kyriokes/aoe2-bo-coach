using System.Collections.ObjectModel;
using BoCoach.Core;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Vision.Regions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BoCoach.App.Coaching;

/// <summary>Un recurso en el panel en vivo: lo que se leyó del HUD y cuántos aldeanos le tocan según la BO.</summary>
public sealed partial class LiveResource(string label) : ObservableObject
{
    public string Label { get; } = label;

    [ObservableProperty] public partial string Stock { get; set; } = "—";
    [ObservableProperty] public partial string Villagers { get; set; } = "";
}

/// <summary>
/// "Lo que ve el coach": todo lo que se leyó de la pantalla en la última captura, para revisar en vivo si está leyendo
/// bien (reloj, recursos, aldeanos, edad, log) y cómo repartir los aldeanos según la BO.
/// </summary>
public sealed partial class LiveReadingViewModel : ObservableObject
{
    private const int MaxEvents = 8;

    public LiveResource Food { get; } = new("Comida");
    public LiveResource Wood { get; } = new("Madera");
    public LiveResource Gold { get; } = new("Oro");
    public LiveResource Stone { get; } = new("Piedra");

    [ObservableProperty] public partial string Clock { get; set; } = "—";
    [ObservableProperty] public partial string Villagers { get; set; } = "—";
    [ObservableProperty] public partial string Idle { get; set; } = "—";
    [ObservableProperty] public partial string Population { get; set; } = "—";
    [ObservableProperty] public partial string Age { get; set; } = "—";
    [ObservableProperty] public partial string Step { get; set; } = "";
    [ObservableProperty] public partial string Problems { get; set; } = "";
    [ObservableProperty] public partial string Usage { get; set; } = "";
    [ObservableProperty] public partial string FoodFloor { get; set; } = "";

    /// <summary>"Cola: aldeano, carretilla, arquero" (lo que se lee de la cola de producción del juego).</summary>
    [ObservableProperty] public partial string Queue { get; set; } = "";

    /// <summary>Los últimos eventos que se leyeron del log, el más nuevo arriba.</summary>
    public ObservableCollection<string> Events { get; } = [];

    public void Apply(CoachFrame frame)
    {
        var hud = frame.Hud;
        var view = frame.View;
        // Si el jugador se quedó quieto al principio, el coach cuenta desde que empezó a jugar y el reloj del juego no.
        Clock = hud?.Clock is { } clock
            ? frame.StartOffset > 0
                ? $"{GameClock.Format(clock)} · la partida empezó a las {GameClock.Format(frame.StartOffset)} (juego {GameClock.Format(view.Time)})"
                : GameClock.Format(clock)
            : "no se lee";

        static string Stock(double? value) => value is { } v ? $"{v:0}" : "no se lee";
        Food.Stock = Stock(hud?.Food);
        Wood.Stock = Stock(hud?.Wood);
        Gold.Stock = Stock(hud?.Gold);
        Stone.Stock = Stock(hud?.Stone);

        var guide = view.Distribution;
        var bo = view.BoDistribution;
        var actual = view.Actual;
        // Lo que se ve en el HUD (con el mod) o, si no, lo que estima la economía; la GD y el reparto de la BO.
        string Plan(Resource r)
        {
            var seen = hud?.VillagersOn is { } on && on.TryGetValue(r, out var n) ? $"hay {n}"
                : actual is { } a ? $"hay ~{a[r]:0}"
                : "hay —";
            return $"aldeanos: {seen} · GD {(guide is { } g ? $"{g[r]:0}" : "—")} (BO {(bo is { } b ? $"{b[r]:0}" : "—")})";
        }
        Food.Villagers = Plan(Resource.Food);
        Wood.Villagers = Plan(Resource.Wood);
        Gold.Villagers = Plan(Resource.Gold);
        Stone.Villagers = Plan(Resource.Stone);

        Villagers = hud?.Villagers is { } v ? $"{v}" : "no se lee";
        Idle = hud?.IdleVillagers is { } idle ? $"{idle}" : "—";
        Population = hud is { Population: { } p, PopulationCap: { } c } ? $"{p}/{c}" : $"{view.Population}/{view.PopulationCap} (estimada)";
        FoodFloor = view.FoodFloor > 0 ? $"mínimo en comida para no parar el centro urbano: {view.FoodFloor}" : "";
        Age = hud?.Age is { } age ? BuildOrderValidator.AgeName(age) : "no se lee";
        Queue = hud?.Queue switch
        {
            null => "Cola de producción: no se lee (calibrá su caja)",
            [] => "Cola de producción: vacía",
            var items => $"Cola de producción: {string.Join(", ", items.Select(i => i.Waiting ? $"{QueueName(i.Key)} (espera)" : QueueName(i.Key)))}",
        };
        Step = view.Current is { } current ? $"Paso {current.Index + 1}/{view.TotalSteps}: {StepText.Describe(current.Step)} ({current.State})" : "BO terminada";
        Problems = hud is { Clipped.Count: > 0 }
            ? $"Números cortados en: {string.Join(", ", hud.Clipped.Select(RegionNames.Label))}"
            : frame.Error is { } error ? $"Error: {error}" : "";
        Usage = $"{frame.Usage} · {frame.FrameMs:0} ms por captura";

        foreach (var e in hud?.NewEvents ?? [])
        {
            Events.Insert(0, $"{Clock}  {e}");
            if (Events.Count > MaxEvents)
                Events.RemoveAt(Events.Count - 1);
        }
    }

    public void Clear() => Events.Clear();

    private static string QueueName(string key) => key switch
    {
        Vision.Hud.IconLibrary.Housed => "sin población",
        Vision.Hud.IconLibrary.Unknown => "?",
        _ when StepText.HasName(key) => StepText.Name(key),
        _ => key,
    };
}
