using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Assisted;

/// <summary>
/// Los centros urbanos extra (spec, sección 3, regla 3): con el objetivo activo, junta su costo (madera y piedra; la
/// piedra va primero en el reparto, como el oro) y desde la edad que los tiene los construye de a uno, con
/// <see cref="Builders"/> constructores, hasta el total que pide el objetivo (contando el inicial). Su producción de
/// aldeanos la hace el Planner: varios centros urbanos sacan aldeanos de la misma cola.
/// </summary>
internal sealed class TownCenterRule : IGoalRule
{
    /// <summary>Cuántos aldeanos construyen cada centro urbano.</summary>
    public const int Builders = 3;

    private const string TownCenter = "town_center";

    public IReadOnlyList<Step> Next(PlanView view, GoalState state)
    {
        if (state.ActiveGoal(view) is not { } index || state.Plan.Goals[index] is not { TownCenters: { } total })
            return [];
        var have = state.Count(view, TownCenter);
        if (have >= total)
        {
            // Ya los pidió otro objetivo: se cumple con esos pasos.
            foreach (var build in view.Steps.Select(s => s.Step).Concat(state.Added).Where(s => s is { Do: StepAction.Build, What: TownCenter }))
                state.Launch(index, build.Id, last: false);
            state.LaunchedGoals.Add(index);
            return [];
        }
        var cost = state.Cost(view, TownCenter);
        state.Planned += cost * (total - have);
        if (view.Age < state.Data.Get(TownCenter).Age || !state.Affordable(view, cost))
            return [];

        var from = state.Builder(view);
        var step = state.Add(
            new Step { Id = state.NewId(), Do = StepAction.Build, What = TownCenter, Builders = Builders, From = from, Assign = from ?? GatherSource.Wood },
            $"Centro urbano {have + 1} de {total}, como pide el objetivo");
        state.Launch(index, step.Id, last: have + 1 >= total);
        return [step];
    }
}
