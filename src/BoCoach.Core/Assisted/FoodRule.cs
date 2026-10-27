using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Assisted;

/// <summary>
/// La comida que se acaba (spec, sección 3, regla 2): ovejas, jabalí (si la apertura lo pide), bayas (si la apertura
/// las pide) y granjas, una por granjero. Con el tiempo justo antes de que se agote la fuente de ahora, los aldeanos
/// nuevos van a la siguiente y los que estaban pasan; a las granjas, en tandas de lo que alcanza la madera (y si se
/// acaba sin madera para todas, los que quedarían parados van a juntarla). El molino,
/// cuando la comida va a las bayas o a las granjas y alcanza para pagarlo.
/// </summary>
internal sealed class FoodRule : IGoalRule
{
    /// <summary>Cuánto antes de que se acabe la fuente se pasa al jabalí o a las bayas.</summary>
    public const double LeadSeconds = 20;

    /// <summary>Cuánto antes se arman las granjas (más lo que tarda en juntarse su madera).</summary>
    public const double FarmLeadSeconds = 30;

    /// <summary>Lo más que se adelantan las granjas para juntar su madera.</summary>
    public const double MaxFarmWoodLead = 300;

    /// <summary>Con cuánta anticipación (además del adelanto) se cuenta la madera de las granjas que vienen en el reparto.</summary>
    public const double FarmWoodHorizon = 60;

    /// <summary>
    /// Con cuánta anticipación se cuenta la madera de las granjas de todos los que juntan comida salvaje (ovejas, jabalí
    /// y bayas en cadena): así el reparto manda aldeanos a la madera antes de que se acabe todo junto.
    /// </summary>
    public const double FarmForesightSeconds = 240;

    private static readonly GatherSource[] Wild = [GatherSource.Sheep, GatherSource.Hunt, GatherSource.Berries];

    public IReadOnlyList<Step> Next(PlanView view, GoalState state)
    {
        if (view.Food is not { } food)
            return [];
        var steps = new List<Step>();
        var farmCost = state.Cost(view, "farm");

        // ¿Se acaba la fuente de ahora? Los nuevos ya van a la siguiente; los que están pasan abajo.
        var current = state.FoodSource;
        if (current != GatherSource.Farm)
        {
            var next = NextSource(current, food, state);
            var on = view.Villagers.GetValueOrDefault(current);
            var seconds = SecondsLeft(view, food, current, on);
            var lead = Lead(view, state, next, on);
            if (seconds <= lead)
                state.FoodSource = next;
            else if (!double.IsInfinity(seconds))
            {
                state.WakeMeAt(view.Time + seconds - lead); // aunque no nazca nadie (el centro urbano investiga una edad)
            }
        }
        ForeseeFarms(view, state, food, farmCost);

        if (state.FoodSource is GatherSource.Berries or GatherSource.Farm && !state.HasOrWillHave(view, "mill")
            && CivBuildings.For(state.Data, state.Civ, "mill") is { } mill)
        {
            var cost = state.Cost(view, mill);
            if (state.Affordable(view, cost))
            {
                var from = Builder(view);
                steps.Add(state.Add(new Step { Id = state.NewId(), Do = StepAction.Build, What = mill, From = from, Assign = from ?? GatherSource.Wood },
                    $"{Capitalize(StepText.Name(mill))}: la comida va a {StepText.Source(state.FoodSource)}"));
            }
            else
            {
                state.Upcoming += cost;
            }
        }

        // Los de una fuente que se acaba (la de antes, o rezagados que llegaron después): a la de ahora.
        foreach (var old in Wild.Where(s => s != state.FoodSource))
        {
            var on = view.Villagers.GetValueOrDefault(old);
            if (on == 0 || state.Pending(view).Any(s => s.From == old && (s.Do == StepAction.Move || s is { Do: StepAction.Build, What: "farm" })))
                continue;
            var seconds = SecondsLeft(view, food, old, on);
            var lead = Lead(view, state, state.FoodSource, on);
            if (seconds > lead)
            {
                if (!double.IsInfinity(seconds))
                    state.WakeMeAt(view.Time + seconds - lead);
                continue;
            }
            var why = $"{Capitalize(StepText.Source(old))} se acaba a las {GameClock.Format(view.Time + seconds)}";
            if (state.FoodSource != GatherSource.Farm)
            {
                steps.Add(state.Add(new Step { Id = state.NewId(), Do = StepAction.Move, Count = on, From = old, Assign = state.FoodSource },
                    $"A {StepText.Source(state.FoodSource)}: {why}"));
                continue;
            }
            var farms = Math.Min(on, state.AffordableCount(view, farmCost));
            if (farms > 0)
            {
                steps.Add(state.Add(new Step { Id = state.NewId(), Do = StepAction.Build, What = "farm", Count = farms, From = old, Assign = GatherSource.Farm },
                    $"{farms} {(farms == 1 ? "granja" : "granjas")}: {why}"));
            }
            if (farms < on && seconds <= FarmLeadSeconds)
            {
                // Se acaba ya y no alcanza la madera para todas: los que quedarían parados van a juntarla.
                steps.Add(state.Add(new Step { Id = state.NewId(), Do = StepAction.Move, Count = on - farms, From = old, Assign = GatherSource.Wood },
                    $"A la madera: {why} y no alcanza para {(on - farms == 1 ? "su granja" : "sus granjas")}"));
            }
            else if (farms < on)
            {
                state.Upcoming += farmCost * (on - farms);
                state.WakeMeAt(view.Time + Math.Min(15, Math.Max(1, seconds - FarmLeadSeconds))); // la próxima tanda, cuando llegue la madera
            }
        }
        return steps;
    }

    /// <summary>
    /// La madera de las granjas que vienen, para el reparto (<see cref="GoalState.Foreseen"/>): si lo que queda de comida
    /// salvaje (la fuente de ahora y las que siguen en la apertura) se junta en <see cref="FarmForesightSeconds"/> o
    /// menos, una granja por cada aldeano en ella que todavía no la tiene pedida.
    /// </summary>
    private static void ForeseeFarms(PlanView view, GoalState state, PlanFoodView food, ResourceAmounts farmCost)
    {
        var sources = new List<GatherSource>();
        for (var source = state.FoodSource; source != GatherSource.Farm; source = NextSource(source, food, state))
            sources.Add(source);
        var wild = Wild.Sum(s => view.Villagers.GetValueOrDefault(s));
        var perSecond = Wild.Sum(s => view.Villagers.GetValueOrDefault(s) * view.Rates.GetValueOrDefault(s));
        if (wild == 0 || perSecond <= 0)
            return;
        var left = Wild.Where(s => s != state.FoodSource && view.Villagers.GetValueOrDefault(s) > 0 || sources.Contains(s))
            .Sum(s => Math.Max(0, food.Remaining.GetValueOrDefault(s)));
        if (left / perSecond > FarmForesightSeconds)
            return;
        var asked = state.Pending(view).Where(s => s is { Do: StepAction.Build, What: "farm" }).Sum(s => s.Count);
        state.Foreseen += farmCost * Math.Max(0, wild - asked);
    }

    /// <summary>Cuánto antes de que se acabe la fuente se pasa a la siguiente: a las granjas, con el tiempo de juntar su madera.</summary>
    private static double Lead(PlanView view, GoalState state, GatherSource next, int villagers) =>
        next == GatherSource.Farm ? FarmLeadSeconds + FarmWoodLead(view, state, villagers) : LeadSeconds;

    /// <summary>
    /// Cuánto antes se piden las granjas para que llegue la madera: la que falta para una granja por aldeano, con lo que
    /// juntan los leñadores de ahora (como mucho <see cref="MaxFarmWoodLead"/>). Así se hacen de a tandas a medida que
    /// llega la madera y los que siguen en la fuente vieja la terminan.
    /// </summary>
    private static double FarmWoodLead(PlanView view, GoalState state, int villagers)
    {
        var wood = state.Cost(view, "farm").Wood * villagers - view.Stock.Wood;
        if (wood <= 0)
            return 0;
        var perSecond = view.Villagers.GetValueOrDefault(GatherSource.Wood) * view.Rates.GetValueOrDefault(GatherSource.Wood);
        return perSecond > 0 ? Math.Min(MaxFarmWoodLead, wood / perSecond) : MaxFarmWoodLead;
    }

    /// <summary>
    /// Si dentro de <paramref name="seconds"/> va a hacer falta pasar a los de alguna comida salvaje que se acaba (un
    /// "mover" o sus granjas): lo que se agregue después de un paso que espera quedaría frenado detrás de él.
    /// </summary>
    public static bool ActsWithin(PlanView view, GoalState state, double seconds)
    {
        if (view.Food is not { } food)
            return false;
        foreach (var source in Wild)
        {
            var on = view.Villagers.GetValueOrDefault(source);
            if (on == 0)
                continue;
            var target = source == state.FoodSource ? NextSource(source, food, state) : state.FoodSource;
            if (SecondsLeft(view, food, source, on) - Lead(view, state, target, on) <= seconds)
                return true;
        }
        return false;
    }

    /// <summary>
    /// A qué fuente de comida mandar un aldeano nuevo que sale dentro de <paramref name="horizon"/> segundos: la de ahora,
    /// o la que sigue si la de ahora se acaba antes.
    /// </summary>
    public static GatherSource TargetFor(PlanView view, GoalState state, double horizon)
    {
        var current = state.FoodSource;
        if (view.Food is not { } food || current == GatherSource.Farm)
            return current;
        return SecondsLeft(view, food, current, view.Villagers.GetValueOrDefault(current)) <= horizon ? NextSource(current, food, state) : current;
    }

    /// <summary>La fuente que sigue a la de ahora: jabalí y bayas si la apertura los pide y el mapa los tiene; si no, granjas.</summary>
    private static GatherSource NextSource(GatherSource current, PlanFoodView food, GoalState state)
    {
        var opening = state.Plan.Opening;
        if (current == GatherSource.Sheep && opening.Boar && food.Remaining.GetValueOrDefault(GatherSource.Hunt) > 0)
            return GatherSource.Hunt;
        if (current is GatherSource.Sheep or GatherSource.Hunt && opening.Berries == "auto" && food.Remaining.GetValueOrDefault(GatherSource.Berries) > 0)
            return GatherSource.Berries;
        return GatherSource.Farm;
    }

    /// <summary>Cuántos segundos le quedan a la fuente con esos aldeanos (0 si está vacía, sin fin si nadie la junta).</summary>
    private static double SecondsLeft(PlanView view, PlanFoodView food, GatherSource source, int villagers)
    {
        var remaining = food.Remaining.GetValueOrDefault(source);
        if (remaining <= 0)
            return 0;
        var perSecond = villagers * view.Rates.GetValueOrDefault(source);
        return perSecond > 0 ? remaining / perSecond : double.PositiveInfinity;
    }

    /// <summary>Quién hace un edificio: un leñador; sin leñadores, de donde haya más gente.</summary>
    private static GatherSource? Builder(PlanView view) =>
        view.Villagers.GetValueOrDefault(GatherSource.Wood) > 0
            ? GatherSource.Wood
            : view.Villagers.Where(v => v.Value > 0).OrderByDescending(v => v.Value).Select(v => (GatherSource?)v.Key).FirstOrDefault();

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
