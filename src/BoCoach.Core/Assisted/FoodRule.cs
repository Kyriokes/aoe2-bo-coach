using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Assisted;

/// <summary>
/// La comida que se acaba (spec, sección 3, regla 2): ovejas, jabalí (si la apertura lo pide), bayas con molino (si la
/// apertura las pide) y granjas, una por granjero. Con el tiempo justo antes de que se agote la fuente de ahora arma
/// la siguiente y pasa a sus aldeanos; los que siguen llegando a una fuente vieja que se acaba también pasan.
/// </summary>
internal sealed class FoodRule : IGoalRule
{
    /// <summary>Cuánto antes de que se acabe la fuente se pasa al jabalí o a las bayas (más el molino si falta).</summary>
    public const double LeadSeconds = 20;

    /// <summary>Cuánto antes se arman las granjas (más el molino si falta).</summary>
    public const double FarmLeadSeconds = 30;

    /// <summary>Con cuánta anticipación se junta la madera de las granjas que vienen.</summary>
    public const double FarmWoodHorizon = 180;

    private static readonly GatherSource[] Wild = [GatherSource.Sheep, GatherSource.Hunt, GatherSource.Berries];

    public IReadOnlyList<Step> Next(PlanView view, GoalState state)
    {
        if (view.Food is not { } food)
            return [];
        var steps = new List<Step>();

        var current = state.FoodSource;
        if (current != GatherSource.Farm)
        {
            var next = NextSource(current, food, state);
            var on = view.Villagers.GetValueOrDefault(current);
            var seconds = SecondsLeft(view, food, current, on);
            var needsMill = next is GatherSource.Berries or GatherSource.Farm && !state.HasOrWillHave(view, "mill");
            var lead = (next == GatherSource.Farm ? FarmLeadSeconds : LeadSeconds) + (needsMill ? MillTime(view, state) : 0);
            if (seconds <= lead)
            {
                var why = $"{Capitalize(StepText.Source(current))} se acaba a las {GameClock.Format(view.Time + seconds)}";
                if (needsMill && CivBuildings.For(state.Data, state.Civ, "mill") is { } mill)
                {
                    steps.Add(state.Add(new Step
                    {
                        Id = state.NewId(), Do = StepAction.Build, What = mill, From = on > 0 ? current : null,
                        Assign = next == GatherSource.Berries ? GatherSource.Berries : current,
                    }, $"{Capitalize(StepText.Name(mill))}: {why}"));
                }
                if (on > 0)
                    steps.Add(Pass(state, current, next, on, why));
                state.FoodSource = next;
            }
            else if (next == GatherSource.Farm && seconds <= FarmWoodHorizon)
            {
                // La madera de las granjas que vienen, para el reparto de los aldeanos.
                state.Planned += state.Civ.Cost(state.Data.Get("farm"), view.Age) * on;
            }
        }

        // Los que siguen llegando a una fuente vieja (estaban en cola antes del cambio): a la de ahora cuando se acaba.
        foreach (var old in Wild.Where(s => s != state.FoodSource))
        {
            var on = view.Villagers.GetValueOrDefault(old);
            if (on == 0 || state.Pending(view).Any(s => s.From == old && s.Do is StepAction.Move or StepAction.Build))
                continue;
            var seconds = SecondsLeft(view, food, old, on);
            if (seconds <= LeadSeconds)
                steps.Add(Pass(state, old, state.FoodSource, on, $"{Capitalize(StepText.Source(old))} se acaba a las {GameClock.Format(view.Time + seconds)}"));
        }
        return steps;
    }

    /// <summary>Pasar a los aldeanos de una fuente a otra: a las granjas, cada uno con la suya; si no, un "mover".</summary>
    private static Step Pass(GoalState state, GatherSource from, GatherSource to, int villagers, string why) =>
        to == GatherSource.Farm
            ? state.Add(new Step { Id = state.NewId(), Do = StepAction.Build, What = "farm", Count = villagers, From = from, Assign = GatherSource.Farm },
                $"Granjas: {why}")
            : state.Add(new Step { Id = state.NewId(), Do = StepAction.Move, Count = villagers, From = from, Assign = to },
                $"A {StepText.Source(to)}: {why}");

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

    /// <summary>Lo que tarda un aldeano en hacer el molino, con la civ.</summary>
    private static double MillTime(PlanView view, GoalState state)
    {
        var mill = CivBuildings.For(state.Data, state.Civ, "mill");
        return mill is null ? 0 : state.Civ.Time(state.Data.Get(mill), view.Age) / state.Civ.BuildRate(view.Age);
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
