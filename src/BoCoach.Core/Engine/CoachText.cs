using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Economy;

namespace BoCoach.Core.Engine;

/// <summary>Los textos del overlay, armados a partir de un <see cref="CoachView"/>.</summary>
public sealed record CoachLines(
    string Current,
    string? Status,
    string? Next,
    IReadOnlyList<string> Running,
    IReadOnlyList<string> Late,
    string? Deviation,
    bool Behind,
    string? Suggestion,
    string? Alert = null,
    string? Idle = null,
    string? Distribution = null);

public static class CoachText
{
    /// <summary>Desvío (en segundos) a partir del cual el objetivo se muestra como atrasado.</summary>
    public const double BehindThreshold = 15;

    public static CoachLines Lines(CoachView view, GameData data)
    {
        var running = view.Running.Select(r => Running(r, view.Time)).ToList();
        if (view.Current is not { } current)
        {
            return new CoachLines(
                view.Finished ? "BO terminada" : "Sin pasos pendientes",
                view.Late.Count > 0 ? "Quedan pasos atrasados" : null,
                null,
                running,
                view.Late.Select(s => StepText.Describe(s.Step)).Take(2).ToList(),
                null,
                false,
                view.Suggestion?.ToString(),
                AlertText(view.Alert),
                IdleText(view.TownCenterIdle),
                DistributionText(view.Distribution));
        }

        var title = StepText.Describe(current.Step);
        if (view.SourceVillagers is { } on && current.Step.Assign is { } source)
            title = $"Aldeanos a {StepText.Source(source)}: {on.Current}/{on.Target}"; // corto y como el contador del HUD
        else if (current.Required > 1 && current.Completed > 0)
            title += $" ({current.Completed}/{current.Required})";

        var deviation = view.Deviations.FirstOrDefault();
        return new CoachLines(
            title,
            Status(view, current, data),
            view.Next is { } next ? $"Después: {StepText.Describe(next.Step)}" : null,
            running,
            view.Late.Select(s => StepText.Describe(s.Step)).Take(2).ToList(),
            deviation?.ToString(),
            deviation is { Delta: > BehindThreshold },
            view.Suggestion?.ToString(),
            AlertText(view.Alert),
            IdleText(view.TownCenterIdle),
            DistributionText(view.Distribution));
    }

    /// <summary>
    /// Cómo están los aldeanos de un recurso contra lo que pide el reparto: <see cref="TargetMatch.Ok"/> si coinciden,
    /// <see cref="TargetMatch.Short"/> si faltan, <see cref="TargetMatch.Over"/> si sobran, o null si no se sabe cuántos hay.
    /// </summary>
    public static TargetMatch? Match(double target, double? actual) => actual switch
    {
        null => null,
        { } a when Math.Round(a) < Math.Round(target) => TargetMatch.Short,
        { } a when Math.Round(a) > Math.Round(target) => TargetMatch.Over,
        _ => TargetMatch.Ok,
    };

    /// <summary>"Reparto: comida 9 · madera 4 · oro 0 · piedra 0".</summary>
    public static string? DistributionText(ResourceAmounts? d) => d is { } v
        ? $"Reparto: comida {v.Food:0} · madera {v.Wood:0} · oro {v.Gold:0} · piedra {v.Stone:0}"
        : null;

    /// <summary>"Centro urbano parado hace 12 s" (lo que pasó de más desde que tendría que haber salido otro aldeano).</summary>
    public static string? IdleText(double? seconds) =>
        seconds is { } s ? $"Centro urbano parado hace {(s < 60 ? $"{s:0} s" : GameClock.Format(s))}" : null;

    /// <summary>Texto de un aviso del log (ver <see cref="Coach.AlertEvents"/>).</summary>
    public static string? AlertText(string? eventId) => eventId switch
    {
        "farm_exhausted" => "Se agotó una granja: resembrala",
        "fish_trap_expired" => "Se agotó una trampa de peces",
        "under_attack" => "¡Te están atacando!",
        "attacked_by_animals" => "Un animal ataca a tus aldeanos",
        _ => null,
    };

    /// <summary>"En curso: Edad Feudal (termina en 1:30)".</summary>
    private static string Running(RunningStep running, double now)
    {
        var step = running.Progress.Step;
        var name = step.What is { } what ? StepText.Name(what) : StepText.Describe(step);
        return running.EndsAt is { } end && end > now
            ? $"En curso: {name} (termina en {GameClock.Format(end - now)})"
            : $"En curso: {name}";
    }

    /// <summary>
    /// Por qué todavía no se puede hacer el paso actual, o cuándo se va a poder. En los pasos de aldeanos no se
    /// muestra lo que falta: el centro urbano los va encolando de a uno y el "faltan 50" cambiaría todo el tiempo.
    /// </summary>
    private static string? Status(CoachView view, StepProgress current, GameData data)
    {
        var step = current.Step;
        var villagers = step.Do == StepAction.Train && step.What == "villager";
        var missing = villagers ? [] : Enum.GetValues<Resource>()
            .Where(r => view.Missing[r] >= 1)
            .Select(r => $"{Math.Ceiling(view.Missing[r]):0} de {Suggestion.Name(r)}")
            .ToList();
        var eta = view.CurrentEta is { } at && at - view.Time >= 5 ? $" (en {GameClock.Format(at - view.Time)})" : "";

        if (missing.Count > 0)
            return $"Faltan {Join(missing)}{eta}";
        if (step.AtPop is { } pop && view.Population < pop)
            return $"A {pop} de población (vas {view.Population})";
        if (step.AtTime is { } time && view.Time < time)
            return $"A los {GameClock.Format(time)}";
        if (step.What is { } what && data.TryGet(what, out var entity) && entity.Pop > 0 && view.Population >= view.PopulationCap)
            return "Sin lugar: hacé una casa";
        return current.State == StepState.Ready ? "Ya se puede" : null;
    }

    private static string Join(IReadOnlyList<string> parts) =>
        parts.Count == 1 ? parts[0] : $"{string.Join(", ", parts.Take(parts.Count - 1))} y {parts[^1]}";
}

/// <summary>Aldeanos de un recurso contra el reparto: verde, rojo o amarillo en el overlay.</summary>
public enum TargetMatch
{
    Ok,
    Short,
    Over,
}
