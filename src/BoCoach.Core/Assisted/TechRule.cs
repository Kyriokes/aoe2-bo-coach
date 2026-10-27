using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Assisted;

/// <summary>
/// Las tecnologías a pedido (spec, sección 3, regla 3): con el objetivo activo, su edificio si falta (como cualquier
/// edificio: la edad, el costo y quién lo hace) y la investigación cuando puede arrancar ya (<see cref="GoalState.CanResearchNow"/>).
/// Mientras no, su costo va al reparto y la regla pide que la despierten; si es del centro urbano, el tramo de aldeanos
/// se corta para dejarlo libre, como para el clic de edad.
/// </summary>
internal sealed class TechRule : IGoalRule
{
    public IReadOnlyList<Step> Next(PlanView view, GoalState state)
    {
        if (state.ActiveGoal(view) is not { } index || state.Plan.Goals[index] is not { Tech: { } tech })
            return [];
        // Ya la pidió otra regla (las mejoras eco) u otro objetivo: se cumple con ese paso.
        if (view.Steps.Select(s => s.Step).Concat(state.Added).FirstOrDefault(s => s.Do == StepAction.Research && s.What == tech) is { } existing)
        {
            state.Launch(index, existing.Id);
            return [];
        }
        var entity = state.Data.Get(tech);
        var building = entity.MadeIn!;
        var cost = state.Civ.Cost(entity, view.Age);
        state.Planned += cost;

        if (building != BuildOrderValidator.StartingBuilding && state.Count(view, building) == 0)
        {
            // Primero lo que pide antes el edificio (el cuartel del establo); si ya está en obra, se espera.
            if (state.ToBuild(view, building) is not { } target)
            {
                state.WakeMeAt(view.Time + 5);
                return [];
            }
            var buildingCost = state.Cost(view, target);
            if (view.Age >= state.Data.Get(target).Age && state.Affordable(view, buildingCost))
            {
                var from = state.Builder(view);
                return [state.Add(
                    new Step { Id = state.NewId(), Do = StepAction.Build, What = target, From = from, Assign = from ?? GatherSource.Wood },
                    target != building
                        ? $"{Capitalize(StepText.Name(target))}: {StepText.Name(building)} lo pide antes, para {StepText.Name(tech)}"
                        : $"{Capitalize(StepText.Name(building))} para {StepText.Name(tech)}")];
            }
            state.Upcoming += buildingCost;
            return [];
        }

        if (state.CanResearchNow(view, tech))
        {
            var step = state.Add(new Step { Id = state.NewId(), Do = StepAction.Research, What = tech },
                $"{Capitalize(StepText.Name(tech))}, como pide el objetivo");
            state.Launch(index, step.Id);
            return [step];
        }
        if (building == BuildOrderValidator.StartingBuilding)
            state.HoldTownCenterFor(view, cost);
        state.WakeMeAt(view.Time + 15);
        return [];
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
