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

    public LiveResource Food { get; } = new(Texts.ResourceFood);
    public LiveResource Wood { get; } = new(Texts.ResourceWood);
    public LiveResource Gold { get; } = new(Texts.ResourceGold);
    public LiveResource Stone { get; } = new(Texts.ResourceStone);

    [ObservableProperty] public partial string Clock { get; set; } = "—";
    [ObservableProperty] public partial string Villagers { get; set; } = "—";
    [ObservableProperty] public partial string Idle { get; set; } = "—";
    [ObservableProperty] public partial string Population { get; set; } = "—";
    [ObservableProperty] public partial string Age { get; set; } = "—";
    [ObservableProperty] public partial string Step { get; set; } = "";

    /// <summary>Los renglones de las líneas, como en el overlay ("Investigar: Telar · faltan 50 de oro").</summary>
    [ObservableProperty] public partial IReadOnlyList<string> Lanes { get; set; } = [];
    [ObservableProperty] public partial string Problems { get; set; } = "";
    [ObservableProperty] public partial string Usage { get; set; } = "";
    [ObservableProperty] public partial string FoodFloor { get; set; } = "";

    /// <summary>"Cola: aldeano, carretilla, arquero" (lo que se lee de la cola de producción del juego).</summary>
    [ObservableProperty] public partial string Queue { get; set; } = "";

    /// <summary>La partida según la pantalla de carga: vos, aliados, rivales y mapa (vacío si no se vio).</summary>
    [ObservableProperty] public partial string Game { get; set; } = "";

    /// <summary>Los últimos eventos que se leyeron del log, el más nuevo arriba.</summary>
    public ObservableCollection<string> Events { get; } = [];

    public void Apply(CoachFrame frame)
    {
        var hud = frame.Hud;
        var view = frame.View;
        // Si el jugador se quedó quieto al principio, el coach cuenta desde que empezó a jugar y el reloj del juego no.
        Clock = hud?.Clock is { } clock
            ? frame.StartOffset > 0
                ? Texts.ClockWithOffset(GameClock.Format(clock), GameClock.Format(frame.StartOffset), GameClock.Format(view.Time))
                : GameClock.Format(clock)
            : Texts.NotRead;

        static string Stock(double? value) => value is { } v ? $"{v:0}" : Texts.NotRead;
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
            var seen = hud?.VillagersOn is { } on && on.TryGetValue(r, out var n) ? Texts.VillagersSeen(n)
                : actual is { } a ? Texts.VillagersEstimated($"{a[r]:0}")
                : Texts.VillagersUnknown;
            return Texts.VillagersPlan(seen, guide is { } g ? $"{g[r]:0}" : "—", bo is { } b ? $"{b[r]:0}" : "—");
        }
        Food.Villagers = Plan(Resource.Food);
        Wood.Villagers = Plan(Resource.Wood);
        Gold.Villagers = Plan(Resource.Gold);
        Stone.Villagers = Plan(Resource.Stone);

        Villagers = hud?.Villagers is { } v ? $"{v}" : Texts.NotRead;
        Idle = hud?.IdleVillagers is { } idle ? $"{idle}" : "—";
        Population = hud is { Population: { } p, PopulationCap: { } c } ? $"{p}/{c}" : Texts.PopulationEstimated(view.Population, view.PopulationCap);
        FoodFloor = view.FoodFloor > 0 ? Texts.FoodFloor((int)view.FoodFloor) : "";
        Age = hud?.Age is { } age ? BuildOrderValidator.AgeName(age) : Texts.NotRead;
        Queue = hud?.Queue switch
        {
            null => Texts.QueueNotRead,
            [] => Texts.QueueEmpty,
            var items => Texts.QueueItems(string.Join(", ", items.Select(i => i.Waiting ? Texts.QueueWaiting(QueueName(i.Key)) : QueueName(i.Key)))),
        };
        Game = GameText(frame);
        Lanes = Core.Engine.CoachText.LaneLines(view, null).Select(l => $"{l.Label}: {l.Text}").ToList();
        Step = view.Abandoned is { } reason ? Texts.Abandoned(reason)
            : view.Current is { } current ? Texts.StepLine(current.Index + 1, view.TotalSteps, StepText.Describe(current.Step), current.State.ToString())
            : view.Posture is { } posture ? Texts.BuildOrderDoneWith(Core.Engine.CoachText.PostureTitle(posture), posture.Derived)
            : Texts.BuildOrderDone;
        Problems = hud is { Clipped.Count: > 0 }
            ? Texts.Clipped(string.Join(", ", hud.Clipped.Select(RegionNames.Label)))
            : frame.Error is { } error ? Texts.LiveError(error) : "";
        Usage = $"{frame.Usage} · {frame.FrameMs:0} ms por captura";

        foreach (var e in hud?.NewEvents ?? [])
        {
            Events.Insert(0, $"{Clock}  {e}");
            if (Events.Count > MaxEvents)
                Events.RemoveAt(Events.Count - 1);
        }
    }

    public void Clear() => Events.Clear();

    private static string GameText(CoachFrame frame)
    {
        if (frame.Loading is not { } loading)
            return "";
        static string Player(Vision.Loading.LoadingPlayer p) => $"{p.Name} ({p.Civ ?? "?"})";
        var map = loading.Map is { } m ? Texts.LoadingMap(m) : "";
        if (frame.Me is not { } me)
            return Texts.LoadingNoName(string.Join(", ", loading.Players.Select(Player)), map);
        var allies = loading.Allies(me);
        return Texts.LoadingMe(Player(me), allies.Count > 0 ? Texts.LoadingAllies(string.Join(", ", allies.Select(Player))) : "",
            string.Join(", ", loading.Opponents(me).Select(Player)), map);
    }

    private static string QueueName(string key) => key switch
    {
        Vision.Hud.IconLibrary.Housed => Texts.QueueHoused,
        Vision.Hud.IconLibrary.Unknown => "?",
        _ when StepText.HasName(key) => StepText.Name(key),
        _ => key,
    };
}
