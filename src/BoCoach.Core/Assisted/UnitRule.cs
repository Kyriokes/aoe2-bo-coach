using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Assisted;

/// <summary>
/// Las unidades (spec, sección 3, regla 3): con el objetivo activo de unidades, construye los N edificios que las producen
/// cuando la edad lo permite y alcanza (de a uno), y pone las unidades en cola apenas hay uno hecho, la edad de la unidad
/// y el costo de la primera: así el paso arranca enseguida y no frena lo de detrás. Los edificios que faltan se siguen
/// construyendo aunque el objetivo ya arrancó, y se suman al terminar (el Planner produce en paralelo con varios iguales).
/// </summary>
internal sealed class UnitRule : IGoalRule
{
    /// <summary>
    /// Cuánto espera hecho el primer edificio a que se pidan los demás antes de que salgan las unidades: con poca
    /// economía, esperar la madera de todos lo dejaba parado minutos.
    /// </summary>
    public const double FirstBuildingWaitSeconds = 90;

    /// <summary>Cuándo se vio hecho el primer edificio de cada objetivo de unidades.</summary>
    private readonly Dictionary<int, double> _firstBuilt = [];

    public IReadOnlyList<Step> Next(PlanView view, GoalState state)
    {
        var steps = new List<Step>();
        // Los edificios que todavía deben los objetivos de unidades ya arrancados (las unidades salieron con el primero).
        foreach (var owed in state.LaunchedGoals.Order())
        {
            if (state.Plan.Goals[owed].Unit is not null && steps.Count == 0 && Producing(view, state, owed)
                && BuildOne(view, state, state.Plan.Goals[owed]) is { } build)
                steps.Add(build);
        }

        if (state.ActiveGoal(view) is not { } index || state.Plan.Goals[index] is not { Unit: { } unit } goal)
            return steps;
        var entity = state.Data.Get(unit);
        var building = entity.MadeIn!;
        var count = goal.Count ?? 1;
        var wanted = goal.Buildings ?? 1;
        var unitCost = state.Civ.Cost(entity, view.Age);
        state.Planned += unitCost * count;
        if (steps.Count == 0 && BuildOne(view, state, goal) is { } first)
            steps.Add(first);

        // Las unidades van con el primer edificio hecho, la edad de la unidad y el costo de la primera: arrancan enseguida
        // (no frenan lo de detrás). Esperan a que estén pedidos todos los edificios (así salen en todos a la vez), salvo
        // que el primero lleve <see cref="FirstBuildingWaitSeconds"/> parado; los que faltan se piden después.
        var built = view.Buildings.GetValueOrDefault(building) > 0;
        if (built)
            _firstBuilt.TryAdd(index, view.Time);
        var allAsked = state.Count(view, building) >= wanted || view.Time - _firstBuilt.GetValueOrDefault(index, view.Time) >= FirstBuildingWaitSeconds;
        if (built && allAsked && view.Age >= entity.Age && state.Affordable(view, unitCost))
        {
            var train = state.Add(new Step { Id = state.NewId(), Do = StepAction.Train, What = unit, Count = count },
                $"{count} {StepText.Name(unit, count)} en {wanted} {StepText.Name(building, wanted)}, como pide el objetivo");
            steps.Add(train);
            state.Launch(index, train.Id);
        }
        else if (state.Count(view, building) >= wanted)
        {
            state.WakeMeAt(view.Time + 15); // el edificio en obra, la edad o el costo de la primera
        }
        return steps;
    }

    /// <summary>Si las unidades de un objetivo ya arrancado todavía no terminaron de salir.</summary>
    private static bool Producing(PlanView view, GoalState state, int index) =>
        state.GoalSteps.GetValueOrDefault(index) is { } ids
        && view.Steps.Any(s => ids.Contains(s.Step.Id) && s.End is null);

    /// <summary>
    /// Uno de los edificios que le faltan a un objetivo de unidades, si la edad lo permite y alcanza; si no, su costo va
    /// al reparto y la regla pide que la despierten. Null si no agrega nada.
    /// </summary>
    private static Step? BuildOne(PlanView view, GoalState state, AssistedGoal goal)
    {
        var unit = goal.Unit!;
        var building = state.Data.Get(unit).MadeIn!;
        var wanted = goal.Buildings ?? 1;
        var have = state.Count(view, building);
        if (have >= wanted)
            return null;
        // Primero lo que pide antes (el cuartel de la galería); si ya está en obra, se espera a que termine.
        if (state.ToBuild(view, building) is not { } target)
        {
            state.WakeMeAt(view.Time + 5);
            return null;
        }
        var cost = state.Cost(view, target);
        if (view.Age < state.Data.Get(target).Age)
        {
            state.Planned += state.CostWithRequirements(view, building);
            return null;
        }
        if (!state.Affordable(view, cost))
        {
            state.Upcoming += cost; // sale cuando alcanza
            return null;
        }
        var from = state.Builder(view);
        var count = goal.Count ?? 1;
        return state.Add(
            new Step { Id = state.NewId(), Do = StepAction.Build, What = target, From = from, Assign = from ?? GatherSource.Wood },
            target != building
                ? $"{Capitalize(StepText.Name(target))}: {StepText.Name(building)} lo pide antes, para {count} {StepText.Name(unit, count)}"
                : $"{Capitalize(StepText.Name(building))} {have + 1} de {wanted} para {count} {StepText.Name(unit, count)}");
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
