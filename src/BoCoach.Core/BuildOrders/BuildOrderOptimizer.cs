using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.BuildOrders;

/// <summary>Una BO mejorada, qué cambió y los tiempos simulados antes y después.</summary>
public sealed record OptimizeResult(BuildOrder BuildOrder, IReadOnlyList<string> Changes, PlanSummary Before, PlanSummary After)
{
    public bool Improved => Changes.Count > 0;
}

/// <summary>Lo que se compara de una simulación: los clics de edad, el fin de la BO y el centro urbano parado.</summary>
public sealed record PlanSummary(IReadOnlyDictionary<string, double> AgeClicks, double End, double IdleTownCenter, double Score)
{
    public string Describe() => string.Join(", ",
        AgeClicks.Select(a => $"{StepText.Name(a.Key)} {GameClock.Format(a.Value)}")
            .Append($"fin {GameClock.Format(End)}")
            .Append($"centro urbano parado {Math.Round(IdleTownCenter)} s"));
}

/// <summary>
/// D7: mejora una BO con el simulador sin cambiar su estrategia (las mismas unidades, edificios, tecnologías y edades,
/// y los mismos aldeanos en total). Busca de a un cambio chico: un aldeano nuevo a madera, oro o piedra en vez de otro
/// recurso (la comida del simulador no se agota), o dos pasos vecinos en
/// el otro orden (una casa antes, el cuartel después de los aldeanos de madera). Se queda con el cambio que más baja
/// el puntaje (la suma de los clics de edad, la mitad del fin de la BO y el tiempo con el centro urbano parado por
/// falta de comida o de población) si lo baja al menos <see cref="MinGain"/> segundos; el simulador es optimista (sin
/// caminatas largas ni errores) y una diferencia más chica no vale. Lo que se cambió queda como texto, por tramos de
/// la BO.
/// </summary>
public sealed class BuildOrderOptimizer(GameData data, PlannerSettings? settings = null)
{
    /// <summary>Lo mínimo que tiene que mejorar el puntaje un cambio para quedarse.</summary>
    public const double MinGain = 3;

    /// <summary>Lo mínimo que tiene que mejorar en total: menos es ruido del simulador y la BO queda como estaba.</summary>
    public const double MinTotalGain = 15;

    private readonly Planner _planner = new(data, settings);
    private readonly BuildOrderValidator _validator = new(data);

    /// <summary>Cuántos cambios se aceptan como mucho.</summary>
    public int MaxChanges { get; init; } = 40;

    public PlanSummary Summarize(BuildOrder bo) => Summarize(_planner.Simulate(bo));

    public OptimizeResult Improve(BuildOrder original)
    {
        var before = Summarize(original);
        var current = ToUnits(original);
        var best = before;
        for (var accepted = 0; accepted < MaxChanges; accepted++)
        {
            (List<Step> Steps, PlanSummary Plan)? winner = null;
            foreach (var candidate in Moves(current))
            {
                var bo = original with { Steps = ToTotals(candidate) };
                if (!_validator.Validate(bo).IsValid)
                    continue;
                var plan = _planner.Simulate(bo);
                if (!plan.Completed)
                    continue;
                var summary = Summarize(plan);
                if (summary.Score <= best.Score - MinGain && (winner is null || summary.Score < winner.Value.Plan.Score))
                    winner = (candidate, summary);
            }
            if (winner is null)
                break;
            (current, best) = (winner.Value.Steps, winner.Value.Plan);
        }

        current = Merge(current);
        var improved = original with { Steps = Renumber(ToTotals(current)) };
        var after = Summarize(improved);
        var changes = after.Score <= before.Score - MinTotalGain ? Describe(ToUnits(original), current) : [];
        return changes.Count == 0
            ? new OptimizeResult(original, [], before, before)
            : new OptimizeResult(improved, changes, before, after);
    }

    private PlanSummary Summarize(PlanResult plan)
    {
        var ages = new[] { "feudal_age", "castle_age", "imperial_age" }
            .Where(a => plan.ClickTime(a) is not null)
            .ToDictionary(a => a, a => plan.ClickTime(a)!.Value);
        var end = plan.Steps.Max(s => s.End ?? 3600);
        var villagerSteps = plan.Steps.Where(s => s.Step is { Do: StepAction.Train, What: "villager" }).Select(s => s.Step.Id).ToHashSet();
        var idle = plan.Blocks
            .Where(b => villagerSteps.Contains(b.StepId) && b.Reason is BlockReason.Resources or BlockReason.Population)
            .Sum(b => b.Duration);
        return new PlanSummary(ages, end, idle, ages.Values.Sum() + end / 2 + idle);
    }

    /// <summary>
    /// Los pasos con la cantidad de aldeanos nuevos de cada paso (y no el total en la fuente): así un aldeano que cambia
    /// de recurso es un cambio en un solo paso y los totales de los pasos siguientes se rehacen solos.
    /// </summary>
    private static List<Step> ToUnits(BuildOrder bo)
    {
        var units = LivePlan.Units(bo);
        return bo.Steps.Select((s, i) => LivePlan.IsVillagerTarget(s) ? s with { Count = units[i], After = [.. s.After] } : s with { After = [.. s.After] }).ToList();
    }

    private static List<Step> ToTotals(List<Step> units)
    {
        var bo = new BuildOrder { Steps = units.Select(s => s with { }).ToList() };
        LivePlan.CountsToTotals(bo);
        return [.. bo.Steps];
    }

    /// <summary>Los cambios chicos a probar.</summary>
    private static IEnumerable<List<Step>> Moves(List<Step> steps)
    {
        // Solo hacia madera, oro y piedra: el simulador no agota las ovejas, el jabalí ni las bayas, y mandar más
        // aldeanos ahí "mejora" una BO que en la partida se quedaría sin comida cerca. Desde la comida sí se puede sacar.
        var sources = new[] { GatherSource.Wood, GatherSource.Gold, GatherSource.Stone };
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if (!LivePlan.IsVillagerTarget(step) || step.Group is not null || step.Count < 1)
                continue;
            // Un aldeano de este paso a otro recurso que la BO ya usa antes (no se abren recursos nuevos: eso cambia la
            // estrategia y pide campamentos).
            var used = steps.Take(i).Where(LivePlan.IsVillagerTarget).Select(s => s.Assign!.Value)
                .Concat(steps.Take(i).Where(s => s.Do is StepAction.Move or StepAction.Build && s.Assign is not null).Select(s => s.Assign!.Value))
                .ToHashSet();
            foreach (var to in sources.Where(s => s != step.Assign && used.Contains(s)))
            {
                var copy = steps.Select(s => s with { }).ToList();
                if (step.Count == 1)
                {
                    copy[i].Assign = to;
                }
                else
                {
                    copy[i].Count--;
                    copy.Insert(i + 1, new Step { Id = $"{step.Id}-{to}-{Guid.NewGuid():N}", Do = StepAction.Train, What = "villager", Count = 1, Assign = to });
                }
                yield return copy;
            }
        }
        for (var i = 0; i + 1 < steps.Count; i++)
        {
            var (a, b) = (steps[i], steps[i + 1]);
            if (a.Group is not null || b.Group is not null || b.After.Contains(a.Id) || a.After.Contains(b.Id))
                continue;
            if (a.Do == StepAction.Research && a.What?.EndsWith("_age", StringComparison.Ordinal) == true
                || b.Do == StepAction.Research && b.What?.EndsWith("_age", StringComparison.Ordinal) == true)
                continue; // los clics de edad quedan donde los puso la guía (y su población)
            if (LivePlan.IsVillagerTarget(a) && LivePlan.IsVillagerTarget(b))
                continue; // dos de aldeanos seguidos: lo hace el cambio de recurso
            if (IsFood(a) || IsFood(b))
                continue; // adelantar aldeanos a la comida es el mismo truco: el simulador no la agota
            var copy = steps.Select(s => s with { }).ToList();
            (copy[i], copy[i + 1]) = (copy[i + 1], copy[i]);
            yield return copy;
        }
    }

    private static bool IsFood(Step step) =>
        step.Assign is { } source && source.Resource() == Resource.Food && (LivePlan.IsVillagerTarget(step) || step.Do == StepAction.Move);

    /// <summary>Junta pasos de aldeanos seguidos al mismo recurso (sin grupo), con sus notas.</summary>
    private static List<Step> Merge(List<Step> steps)
    {
        var merged = new List<Step>();
        var referenced = steps.SelectMany(s => s.After).ToHashSet();
        foreach (var step in steps)
        {
            if (merged.Count > 0 && merged[^1] is var last && LivePlan.IsVillagerTarget(last) && LivePlan.IsVillagerTarget(step)
                && last.Assign == step.Assign && last.Group is null && step.Group is null && step.AtPop is null && step.AtTime is null
                && step.After.Count == 0 && !referenced.Contains(step.Id))
            {
                last.Count += step.Count;
                if (!string.IsNullOrEmpty(step.Note))
                    last.Note = string.IsNullOrEmpty(last.Note) ? step.Note : $"{last.Note}. {step.Note}";
                continue;
            }
            merged.Add(step with { });
        }
        return merged;
    }

    /// <summary>Ids "s1", "s2"... en orden, con los 'after' apuntando a los mismos pasos.</summary>
    private static List<Step> Renumber(List<Step> steps)
    {
        var ids = steps.Select((s, i) => (s.Id, New: $"s{i + 1}")).Where(x => x.Id.Length > 0).GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().New);
        return steps.Select((s, i) => s with { Id = $"s{i + 1}", After = s.After.Select(a => ids.GetValueOrDefault(a, a)).ToList() }).ToList();
    }

    /// <summary>
    /// Qué cambió, por tramos: los aldeanos nuevos de cada recurso antes de cada paso que no es de aldeanos (casas,
    /// edificios, investigaciones, unidades), y los pasos que cambiaron de orden.
    /// </summary>
    private static List<string> Describe(List<Step> before, List<Step> after)
    {
        var changes = new List<string>();
        var anchorsBefore = Anchors(before);
        var anchorsAfter = Anchors(after);
        var orderBefore = anchorsBefore.Select(a => a.Key).ToList();
        var orderAfter = anchorsAfter.Select(a => a.Key).ToList();
        var byKey = anchorsBefore.ToDictionary(a => a.Key);
        foreach (var a in anchorsAfter)
        {
            if (!byKey.TryGetValue(a.Key, out var b))
                continue;
            var diff = a.Villagers.Keys.Union(b.Villagers.Keys)
                .Select(s => (Source: s, Delta: a.Villagers.GetValueOrDefault(s) - b.Villagers.GetValueOrDefault(s)))
                .Where(d => d.Delta != 0)
                .OrderByDescending(d => d.Delta)
                .ToList();
            if (diff.Count > 0)
                changes.Add($"{(a.Key == "fin" ? "Al final de la BO" : $"Antes de \"{a.Text}\"")}: {string.Join(", ", diff.Select(d => $"{(d.Delta > 0 ? "+" : "")}{d.Delta} en {StepText.Source(d.Source)}"))}");
        }
        for (var k = 0; k < orderAfter.Count; k++)
        {
            if (k < orderBefore.Count && orderBefore[k] == orderAfter[k])
                continue;
            var moved = anchorsAfter[k];
            var earlier = orderBefore.IndexOf(moved.Key) > k;
            if (earlier && k + 1 < anchorsAfter.Count)
                changes.Add($"\"{moved.Text}\" antes que \"{anchorsAfter[k + 1].Text}\"");
        }
        return changes;
    }

    private sealed record Anchor(string Key, string Text, Dictionary<GatherSource, int> Villagers);

    /// <summary>Los pasos que no son de aldeanos, con los aldeanos nuevos de cada recurso desde el anterior.</summary>
    private static List<Anchor> Anchors(List<Step> steps)
    {
        var anchors = new List<Anchor>();
        var pending = new Dictionary<GatherSource, int>();
        var seen = new Dictionary<string, int>();
        foreach (var step in steps)
        {
            if (LivePlan.IsVillagerTarget(step))
            {
                pending[step.Assign!.Value] = pending.GetValueOrDefault(step.Assign.Value) + step.Count;
                continue;
            }
            var text = StepText.Describe(step);
            seen[text] = seen.GetValueOrDefault(text) + 1;
            anchors.Add(new Anchor($"{text}#{seen[text]}", text, pending));
            pending = [];
        }
        anchors.Add(new Anchor("fin", "el final de la BO", pending)); // los aldeanos después del último paso
        return anchors;
    }
}
