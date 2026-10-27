using BoCoach.Core.BuildOrders;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Assisted;

/// <summary>
/// D12: la política del modo asistido. En cada punto de decisión corre las reglas en orden (apertura, casas,
/// campamentos, comida, edad, aldeanos) y devuelve lo que agregaron; cada regla ve lo que agregaron las anteriores (<see cref="GoalState.Added"/>).
/// </summary>
internal sealed class GoalPolicy(GoalState state, IReadOnlyList<IGoalRule> rules) : IStepPolicy
{
    public GoalPolicy(GoalState state)
        : this(state, [new OpeningRule(), new HouseRule(), new CampRule(), new FoodRule(), new AgeRule(), new VillagerRule()])
    {
    }

    public double? WakeAt => state.WakeAt;

    public IReadOnlyList<Step> Next(PlanView view)
    {
        state.Added.Clear();
        state.WakeAt = null;
        state.Planned = default;
        state.Upcoming = default;
        state.VillagersBeforeClick = null;
        foreach (var rule in rules)
            rule.Next(view, state);
        return [.. state.Added];
    }
}
