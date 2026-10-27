using BoCoach.Core.BuildOrders;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Assisted;

/// <summary>
/// D12: la política del modo asistido. En cada punto de decisión corre las reglas en orden (apertura, casas, comida,
/// edad, aldeanos) y devuelve lo que agregaron; cada regla ve lo que agregaron las anteriores (<see cref="GoalState.Added"/>).
/// </summary>
internal sealed class GoalPolicy(GoalState state, IReadOnlyList<IGoalRule> rules) : IStepPolicy
{
    public GoalPolicy(GoalState state)
        : this(state, [new OpeningRule(), new HouseRule(), new VillagerRule()])
    {
    }

    public IReadOnlyList<Step> Next(PlanView view)
    {
        state.Added.Clear();
        foreach (var rule in rules)
            rule.Next(view, state);
        return [.. state.Added];
    }
}
