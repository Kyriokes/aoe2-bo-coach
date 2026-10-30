using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;

namespace BoCoach.Core.Engine;

/// <summary>Una rama en curso para el overlay.</summary>
/// <param name="Title">La señal que la disparó ("Si el rival hace clic en Feudal") o su nota.</param>
/// <param name="Current">El paso de la rama que toca (o la producción que sigue), o null si terminó.</param>
public sealed record BranchView(string Title, string? Note, StepProgress? Current, int Done, int Total);

/// <summary>
/// Las ramas de la BO (D3): cuando llega su señal (una vez por rama, antes de su <see cref="Branch.Before"/>), sus
/// pasos se siguen con su propio estado desde ese momento (lo hecho antes no cuenta). Se muestra la más nueva sin
/// terminar.
/// </summary>
public sealed class Branches(BuildOrder bo, GameData data, EventCatalog? events)
{
    private sealed record Run(Branch Branch, PlayerState State, Tracker Tracker);

    private readonly List<Run> _runs = [];
    private readonly HashSet<Branch> _fired = [];

    /// <summary>Dispara las ramas de estas señales nuevas.</summary>
    public void Trigger(IEnumerable<GameSignal> signals, PlayerState state)
    {
        foreach (var signal in signals)
        {
            foreach (var branch in bo.Branches ?? [])
            {
                if (branch.On != signal.Kind || branch.Steps.Count == 0 || _fired.Contains(branch)
                    || branch.Before is { } before && signal.Time > before)
                    continue;
                _fired.Add(branch);
                var own = new PlayerState(state.Population, state.PopulationCap, state.Villagers);
                var steps = new BuildOrder { Name = bo.Name, Civ = bo.Civ, Steps = branch.Steps };
                _runs.Add(new Run(branch, own, new Tracker(steps, data, events)));
            }
        }
    }

    /// <summary>Lleva las ramas en curso con el snapshot (cada una ve solo lo de después de dispararse).</summary>
    public void Update(GameSnapshot snapshot)
    {
        foreach (var run in _runs)
        {
            run.State.Apply(snapshot);
            run.Tracker.Update(run.State);
        }
        _runs.RemoveAll(r => r.Tracker.Steps.All(s => s.State is StepState.Done or StepState.Skipped));
    }

    /// <summary>La rama más nueva sin terminar, o null.</summary>
    public BranchView? View()
    {
        if (_runs.LastOrDefault() is not { } run)
            return null;
        var steps = run.Tracker.Steps;
        return new BranchView(
            Branch.Signals.GetValueOrDefault(run.Branch.On, run.Branch.On),
            run.Branch.Note,
            // La rama se muestra entera aunque su Tracker no sepa la edad ni los edificios (solo ve lo de después de
            // dispararse): no pasa por la elegibilidad de las líneas.
            run.Tracker.Steps.FirstOrDefault(s => s.State is StepState.Pending or StepState.Ready && !run.Tracker.Producing.Any(p => p.Index == s.Index))
                ?? run.Tracker.Producing.FirstOrDefault(),
            steps.Count(s => s.State is StepState.Done or StepState.Skipped),
            steps.Count);
    }

    /// <summary>"Atrás"/"Saltear" sobre la rama: se la da por terminada.</summary>
    public bool Dismiss()
    {
        if (_runs.Count == 0)
            return false;
        _runs.RemoveAt(_runs.Count - 1);
        return true;
    }
}
