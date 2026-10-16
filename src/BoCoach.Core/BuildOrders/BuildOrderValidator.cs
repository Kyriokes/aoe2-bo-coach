using BoCoach.Core.Data;

namespace BoCoach.Core.BuildOrders;

public enum Severity
{
    Warning,
    Error,
}

/// <param name="StepId">Paso con el problema, o null si es de la BO en general.</param>
public sealed record ValidationIssue(Severity Severity, string? StepId, string Message)
{
    public override string ToString() =>
        $"{(Severity == Severity.Error ? "Error" : "Aviso")}{(StepId is null ? "" : $" en el paso {StepId}")}: {Message}";
}

public sealed record ValidationResult(IReadOnlyList<ValidationIssue> Issues)
{
    public bool IsValid => Issues.All(i => i.Severity != Severity.Error);
    public IEnumerable<ValidationIssue> Errors => Issues.Where(i => i.Severity == Severity.Error);
    public IEnumerable<ValidationIssue> Warnings => Issues.Where(i => i.Severity == Severity.Warning);
}

/// <summary>Revisa que una BO tenga sentido contra los datos del juego. No simula tiempos (eso es del Planner).</summary>
public sealed class BuildOrderValidator(GameData gameData, EventCatalog? events = null)
{
    public const int MaxPopulation = 200;

    /// <summary>Edificio con el que arranca la partida.</summary>
    public const string StartingBuilding = "town_center";

    // Reglas del juego: para subir de edad hacen falta 2 edificios de la edad actual.
    private static readonly Dictionary<string, (Age Requires, string[] Buildings)> AgeUpRequirements = new()
    {
        ["feudal_age"] = (Age.Dark, ["barracks", "mill", "lumber_camp", "mining_camp", "dock"]),
        ["castle_age"] = (Age.Feudal, ["archery_range", "stable", "blacksmith", "market"]),
    };

    private static readonly Dictionary<string, Age> AgeTechs = new()
    {
        ["feudal_age"] = Age.Feudal,
        ["castle_age"] = Age.Castle,
        ["imperial_age"] = Age.Imperial,
    };

    public ValidationResult Validate(BuildOrder bo)
    {
        var issues = new List<ValidationIssue>();
        void Error(string? step, string message) => issues.Add(new(Severity.Error, step, message));
        void Warn(string? step, string message) => issues.Add(new(Severity.Warning, step, message));

        if (string.IsNullOrWhiteSpace(bo.Name))
            Error(null, "La build order no tiene nombre.");
        if (bo.Steps.Count == 0)
            Error(null, "La build order no tiene pasos.");
        if (!string.Equals(bo.Civ, "any", StringComparison.OrdinalIgnoreCase) && gameData.Civs.Count > 0 && !gameData.IsKnownCiv(bo.Civ))
            Warn(null, $"La civ '{bo.Civ}' no está en los datos del juego: se simula sin sus bonificaciones (usá, por ejemplo, 'britons' o 'any').");

        var ids = new HashSet<string>();
        foreach (var step in bo.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Id))
                Error(null, $"Hay un paso sin id ({Describe(step)}).");
            else if (!ids.Add(step.Id))
                Error(step.Id, $"El id '{step.Id}' está repetido.");
        }

        var age = Age.Dark;
        var built = new HashSet<string> { StartingBuilding };
        foreach (var step in bo.Steps)
        {
            var id = string.IsNullOrWhiteSpace(step.Id) ? null : step.Id;
            ValidateStep(step, id, age, built, ids, Error, Warn);

            if (step.Do == StepAction.Build && step.What is not null)
                built.Add(step.What);
            if (step.Do == StepAction.Research && step.What is not null && AgeTechs.TryGetValue(step.What, out var next))
                age = next;
        }

        foreach (var cycle in FindCycles(bo.Steps))
            Error(cycle[0], $"Dependencia circular en 'after': {string.Join(" → ", cycle)}.");

        ValidateTargets(bo.Targets, Error);
        return new ValidationResult(issues);
    }

    private void ValidateStep(
        Step step, string? id, Age age, HashSet<string> built, HashSet<string> ids,
        Action<string?, string> error, Action<string?, string> warn)
    {
        if (step.Count < 1)
            error(id, $"'count' tiene que ser 1 o más (es {step.Count}).");
        if (step.AtPop is < 1 or > MaxPopulation)
            error(id, $"'at_pop' tiene que estar entre 1 y {MaxPopulation} (es {step.AtPop}).");
        if (step.AtTime is < 0)
            error(id, $"'at_time' no puede ser negativo (es {step.AtTime}).");
        if (step.Builders is not null && step.Do != StepAction.Build)
            error(id, "'builders' solo se usa en pasos 'build'.");
        else if (step.Builders is < 1)
            error(id, $"'builders' tiene que ser 1 o más (es {step.Builders}).");

        foreach (var dep in step.After)
        {
            if (dep == step.Id)
                error(id, "El paso depende de sí mismo en 'after'.");
            else if (!ids.Contains(dep))
                error(id, $"'after' menciona el paso '{dep}', que no existe.");
        }

        if (step.ConfirmBy is not null && events is not null && !events.Contains(step.ConfirmBy))
            error(id, $"'confirm_by' usa el evento '{step.ConfirmBy}', que no está en events.json.");

        if (step.Do == StepAction.Move)
        {
            ValidateMove(step, id, error);
            return;
        }

        if (string.IsNullOrWhiteSpace(step.What))
        {
            error(id, $"Falta 'what': ¿qué hay que {Verb(step.Do)}?");
            return;
        }
        if (!gameData.TryGet(step.What, out var entity))
        {
            error(id, $"'{step.What}' no existe en los datos del juego.");
            return;
        }

        var expectedKind = step.Do switch
        {
            StepAction.Train => EntityKind.Unit,
            StepAction.Build => EntityKind.Building,
            _ => EntityKind.Tech,
        };
        if (entity.Kind != expectedKind)
        {
            error(id, $"No se puede {Verb(step.Do)} '{step.What}': es {KindName(entity.Kind)}.");
            return;
        }

        if (entity.Age > age)
            error(id, $"'{step.What}' recién está en {AgeName(entity.Age)}, y en este paso todavía estás en {AgeName(age)}.");

        if (entity.MadeIn is { } place && !built.Contains(place))
            warn(id, $"'{step.What}' se hace en '{place}', y la BO no lo construye antes de este paso.");

        if (step.Do == StepAction.Train)
        {
            if (step.What == "villager" && step.Assign is null)
                error(id, "Falta 'assign': ¿a qué recurso van los aldeanos nuevos?");
            if (step.What != "villager" && step.Assign is not null)
                error(id, $"'assign' solo tiene sentido para aldeanos, no para '{step.What}'.");
        }

        if (step.Do == StepAction.Research && AgeUpRequirements.TryGetValue(step.What, out var req) && age == req.Requires)
        {
            var count = req.Buildings.Count(built.Contains);
            if (count < 2)
            {
                warn(id, $"Para {AgeName(AgeTechs[step.What])} hacen falta 2 edificios de {AgeName(req.Requires)} " +
                         $"({string.Join(", ", req.Buildings)}); antes de este paso la BO construye {count}.");
            }
        }
    }

    private static void ValidateMove(Step step, string? id, Action<string?, string> error)
    {
        if (step.What is not null)
            error(id, "Un paso 'move' no usa 'what'.");
        if (step.From is null)
            error(id, "Falta 'from': ¿de qué recurso salen los aldeanos?");
        if (step.Assign is null)
            error(id, "Falta 'assign': ¿a qué recurso van los aldeanos?");
        if (step.From is not null && step.From == step.Assign)
            error(id, $"'from' y 'assign' son el mismo recurso ({step.From}).");
    }

    private static void ValidateTargets(Targets? targets, Action<string?, string> error)
    {
        if (targets is null)
            return;
        if (targets.FeudalClickTime is <= 0)
            error(null, "targets.feudal_click_time tiene que ser mayor a 0.");
        if (targets.CastleClickTime is <= 0)
            error(null, "targets.castle_click_time tiene que ser mayor a 0.");
        if (targets.FeudalClickTime is { } f && targets.CastleClickTime is { } c && c <= f)
            error(null, "targets.castle_click_time tiene que ser posterior a feudal_click_time.");
        foreach (var (name, d) in new[] { ("villagers_at_feudal", targets.VillagersAtFeudal), ("villagers_at_castle", targets.VillagersAtCastle) })
        {
            if (d is not null && (d.Food < 0 || d.Wood < 0 || d.Gold < 0 || d.Stone < 0))
                error(null, $"targets.{name} no puede tener números negativos.");
        }
    }

    /// <summary>Ciclos en el grafo de 'after', cada uno como lista de ids cerrada (a → b → a).</summary>
    private static List<List<string>> FindCycles(IReadOnlyList<Step> steps)
    {
        var deps = steps
            .Where(s => !string.IsNullOrWhiteSpace(s.Id))
            .GroupBy(s => s.Id)
            .ToDictionary(g => g.Key, g => g.First().After);
        var state = new Dictionary<string, int>(); // 1 = visitando, 2 = listo
        var path = new List<string>();
        var cycles = new List<List<string>>();

        void Visit(string id)
        {
            state[id] = 1;
            path.Add(id);
            foreach (var dep in deps[id].Where(deps.ContainsKey))
            {
                if (dep == id)
                    continue; // ya se informa como "depende de sí mismo"
                if (state.GetValueOrDefault(dep) == 1)
                    cycles.Add([.. path.Skip(path.IndexOf(dep)), dep]);
                else if (state.GetValueOrDefault(dep) == 0)
                    Visit(dep);
            }
            path.RemoveAt(path.Count - 1);
            state[id] = 2;
        }

        foreach (var id in deps.Keys.Where(id => state.GetValueOrDefault(id) == 0))
            Visit(id);
        return cycles;
    }

    private static string Describe(Step step) => step.Do == StepAction.Move
        ? $"move {step.From} → {step.Assign}"
        : $"{step.Do.ToString().ToLowerInvariant()} {step.What}";

    private static string Verb(StepAction action) => action switch
    {
        StepAction.Train => "entrenar",
        StepAction.Build => "construir",
        StepAction.Research => "investigar",
        _ => "mover",
    };

    private static string KindName(EntityKind kind) => kind switch
    {
        EntityKind.Unit => "una unidad",
        EntityKind.Building => "un edificio",
        _ => "una tecnología",
    };

    public static string AgeName(Age age) => age switch
    {
        Age.Dark => "Edad Oscura",
        Age.Feudal => "Edad Feudal",
        Age.Castle => "Edad de los Castillos",
        _ => "Edad Imperial",
    };
}
