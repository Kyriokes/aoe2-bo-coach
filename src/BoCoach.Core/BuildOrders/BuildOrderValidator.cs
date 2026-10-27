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
/// <param name="maps">Los mapas (data/maps.json) para avisar si la BO usa algo que su mapa no tiene; sin ellos no se mira el mapa.</param>
public sealed class BuildOrderValidator(GameData gameData, EventCatalog? events = null, Maps? maps = null)
{
    public const int MaxPopulation = 200;

    /// <summary>
    /// Tope de 'count' y de 'builders' (la población máxima: nada se hace de a más). Sin tope, una BO bajada de la web
    /// con un número enorme agota la memoria del Planner, que encola una entrada por unidad, en la app y en el navegador.
    /// </summary>
    public const int MaxCount = MaxPopulation;

    /// <summary>Edificio con el que arranca la partida.</summary>
    public const string StartingBuilding = "town_center";

    // Reglas del juego: para subir de edad hacen falta 2 edificios de la edad actual.
    private static readonly Dictionary<string, (Age Requires, string[] Buildings)> AgeUpRequirements = new()
    {
        ["feudal_age"] = (Age.Dark, ["barracks", "mill", "lumber_camp", "mining_camp", "dock"]),
        ["castle_age"] = (Age.Feudal, ["archery_range", "stable", "blacksmith", "market"]),
        // D12: Imperial pide 2 edificios de Castillos o un castillo solo (que cuenta por los dos).
        ["imperial_age"] = (Age.Castle, ["university", "monastery", "siege_workshop", "castle"]),
    };

    /// <summary>El edificio que alcanza solo para subir a una edad (el castillo para Imperial).</summary>
    private static readonly Dictionary<string, string> AgeUpAlone = new() { ["imperial_age"] = "castle" };

    private static readonly Dictionary<string, Age> AgeTechs = new()
    {
        ["feudal_age"] = Age.Feudal,
        ["castle_age"] = Age.Castle,
        ["imperial_age"] = Age.Imperial,
    };

    /// <summary>Los edificios de la edad anterior que pide una edad (2 de ellos), o null si no hay regla cargada.</summary>
    public static (Age Requires, IReadOnlyList<string> Buildings)? AgeUpRequirement(string ageTech) =>
        AgeUpRequirements.TryGetValue(ageTech, out var r) ? (r.Requires, r.Buildings) : null;

    /// <summary>
    /// Cuántos de los 2 edificios que pide la edad ya están entre <paramref name="builtKeys"/>: cada clave distinta de la
    /// lista cuenta 1 y el castillo cuenta 2 para Imperial; como mucho 2. 0 si no es una tecnología de edad.
    /// </summary>
    public static int AgeUpProgress(string ageTech, IEnumerable<string> builtKeys)
    {
        if (!AgeUpRequirements.TryGetValue(ageTech, out var req))
            return 0;
        var built = builtKeys.ToHashSet();
        var count = req.Buildings.Count(built.Contains);
        if (AgeUpAlone.TryGetValue(ageTech, out var alone) && built.Contains(alone))
            count++;
        return Math.Min(2, count);
    }

    /// <summary>El edificio que por sí solo vale por los 2 que pide la edad (el castillo para Imperial), o null.</summary>
    public static string? AgeUpAloneBuilding(string ageTech) => AgeUpAlone.GetValueOrDefault(ageTech);

    /// <summary>La edad a la que lleva una tecnología de edad ("castle_age" → Castillos), o null si no es una.</summary>
    public static Age? AgeOf(string tech) => AgeTechs.TryGetValue(tech, out var age) ? age : null;

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

        var map = MapOf(bo, Warn);
        var age = Age.Dark;
        var built = new HashSet<string> { StartingBuilding };
        foreach (var step in bo.Steps)
        {
            var id = string.IsNullOrWhiteSpace(step.Id) ? null : step.Id;
            ValidateStep(step, id, age, built, ids, Error, Warn, gameData.Civ(bo.Civ));
            if (step.What is { } what && step.Do != StepAction.Move && !gameData.CanMake(bo.Civ, what))
                Error(id, $"La civ {CivName(bo.Civ)} no tiene {StepText.Name(what)} (según su árbol de tecnologías).");
            if (map is not null)
                ValidateMap(step, id, map, Warn);

            if (step.Do == StepAction.Build && step.What is not null)
            {
                built.Add(step.What);
                if (gameData.TryGet(step.What, out var building))
                    built.UnionWith(building.CountsAs); // el Settlement de los incas vale por campamentos, molino y casa
            }
            if (step.Do == StepAction.Research && step.What is not null && AgeTechs.TryGetValue(step.What, out var next))
                age = next;
        }

        // Formato 2: un paso de aldeanos dice cuántos tiene que haber en la fuente; si ya los hay, no pide ninguno.
        var units = ids.Count == bo.Steps.Count ? Engine.LivePlan.Units(bo) : [];
        for (var i = 0; i < units.Length; i++)
        {
            if (units[i] == 0 && Engine.LivePlan.IsVillagerTarget(bo.Steps[i]))
                Warn(bo.Steps[i].Id, $"Antes de este paso ya hay {bo.Steps[i].Count} o más aldeanos en {StepText.Source(bo.Steps[i].Assign!.Value)}: no entrena ninguno (la cantidad es el total en el recurso, como en el HUD).");
        }

        // "A N de población" que los pasos anteriores nunca alcanzan: el paso no arranca y el coach espera para siempre.
        var population = Engine.PlanStart.For(gameData.Civ(bo.Civ), gameData).Population;
        for (var i = 0; i < units.Length; i++)
        {
            var step = bo.Steps[i];
            if (step.AtPop is { } atPop && atPop > population)
            {
                Warn(step.Id, $"La población nunca llega a {atPop}: con los pasos anteriores hay {population} (aldeanos y unidades, " +
                              $"con el explorador). Faltan {atPop - population} aldeanos antes, o bajá 'a N de población'.");
            }
            if (step.Do == StepAction.Train && step.What is not null && gameData.TryGet(step.What, out var unit))
                population += units[i] * Math.Max(1, unit.Pop);
        }

        foreach (var cycle in FindCycles(bo.Steps))
            Error(cycle[0], $"Dependencia circular en 'after': {string.Join(" → ", cycle)}.");

        // Pasos mixtos: las metas de un grupo van seguidas, y un grupo de una sola meta es un paso simple.
        foreach (var group in bo.Steps.Select((s, i) => (s, i)).Where(x => x.s.Group is not null).GroupBy(x => x.s.Group!))
        {
            var indexes = group.Select(x => x.i).ToList();
            if (indexes[^1] - indexes[0] + 1 != indexes.Count)
                Error(group.First().s.Id, $"Las metas del paso mixto '{group.Key}' tienen que ir seguidas, sin otros pasos en el medio.");
            else if (indexes.Count == 1)
                Warn(group.First().s.Id, $"El paso mixto '{group.Key}' tiene una sola meta: es un paso simple.");
        }

        ValidateTargets(bo.Targets, Error);
        ValidateAssisted(bo.Assisted, Error);
        ValidatePosture(bo.Posture, age, Warn);
        ValidateBranches(bo.Branches, Error, Warn);
        return new ValidationResult(issues);
    }

    private string CivName(string civ) =>
        gameData.Civ(civ).Names.TryGetValue("mx", out var name) ? name : civ;

    /// <summary>El mapa de la BO si hay que mirarlo: con mapas cargados, un mapa puesto y conocido (uno desconocido avisa).</summary>
    private GameMap? MapOf(BuildOrder bo, Action<string?, string> warn)
    {
        if (maps is null || Maps.IsAny(bo.Map))
            return null;
        if (maps.Get(bo.Map) is { } map)
            return map;
        warn(null, $"El mapa '{bo.Map}' no está en maps.json: se revisa como si fuera cualquier mapa (usá, por ejemplo, 'arabia' o 'any').");
        return null;
    }

    /// <summary>
    /// D8: lo que el mapa no tiene. Un aviso y no un error, porque la tabla de mapas es aproximada y el Planner simula
    /// igual: una fuente que el mapa no tiene (pesca en Arabia), o el muelle y lo que sale de él sin agua.
    /// </summary>
    private void ValidateMap(Step step, string? id, GameMap map, Action<string?, string> warn)
    {
        foreach (var source in new[] { step.Assign, step.From }.OfType<GatherSource>().Distinct())
        {
            if (!map.Has(source))
                warn(id, $"El mapa {map.Name} no tiene {StepText.Source(source)}.");
        }
        if (map.Water || step.What is null || !gameData.TryGet(step.What, out var entity))
            return;
        if (step.What == "dock" || entity.MadeIn == "dock")
            warn(id, $"El mapa {map.Name} no tiene agua: no hay {StepText.Name(step.What, step.Count)}.");
    }

    /// <summary>
    /// Las ramas: una señal conocida y pasos que existan. Sus pasos se hacen en cualquier momento de la partida, así
    /// que no se revisan la edad ni los edificios que piden.
    /// </summary>
    private void ValidateBranches(IReadOnlyList<Branch>? branches, Action<string?, string> error, Action<string?, string> warn)
    {
        foreach (var branch in branches ?? [])
        {
            if (!Branch.Signals.TryGetValue(branch.On, out var name))
            {
                error(null, $"Rama con la señal '{branch.On}', que no existe (son: {string.Join(", ", Branch.Signals.Keys)}).");
                continue;
            }
            if (branch.Steps.Count == 0)
                warn(null, $"La rama \"{name}\" no tiene pasos.");
            if (branch.Before is < 0)
                error(null, $"Rama \"{name}\": 'before' no puede ser negativo.");
            var ids = branch.Steps.Select(s => s.Id).ToHashSet();
            var everything = gameData.Entities.Values.Where(e => e.Kind == EntityKind.Building).Select(e => e.Key).ToHashSet();
            foreach (var step in branch.Steps)
                ValidateStep(step, string.IsNullOrWhiteSpace(step.Id) ? null : step.Id, Age.Imperial, everything, ids, error, warn);
        }
    }

    /// <summary>La postura de cierre: unidades que se entrenan en algún edificio y una edad posterior a la de la BO.</summary>
    private void ValidatePosture(ClosingPosture? posture, Age age, Action<string?, string> warn)
    {
        if (posture is null)
            return;
        foreach (var unit in posture.Queue)
        {
            if (!gameData.TryGet(unit, out var entity) || entity.Kind != EntityKind.Unit || entity.MadeIn is null)
                warn(null, $"Postura: '{unit}' no es una unidad que se entrene en un edificio.");
        }
        if (posture.NextAge is { } next && (AgeOf(next) is not { } target || target <= age))
            warn(null, $"Postura: '{next}' no es una edad posterior a la última de la BO ({AgeName(age)}).");
    }

    private void ValidateStep(
        Step step, string? id, Age age, HashSet<string> built, HashSet<string> ids,
        Action<string?, string> error, Action<string?, string> warn, CivBonuses? civ = null)
    {
        if (step.Count is < 1 or > MaxCount)
            error(id, $"'count' tiene que estar entre 1 y {MaxCount} (es {step.Count}).");
        if (step.AtPop is < 1 or > MaxPopulation)
            error(id, $"'at_pop' tiene que estar entre 1 y {MaxPopulation} (es {step.AtPop}).");
        if (step.AtTime is < 0)
            error(id, $"'at_time' no puede ser negativo (es {step.AtTime}).");
        if (step.Builders is not null && step.Do != StepAction.Build)
            error(id, "'builders' solo se usa en pasos 'build'.");
        else if (step.Builders is < 1 or > MaxCount)
            error(id, $"'builders' tiene que estar entre 1 y {MaxCount} (es {step.Builders}).");

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
        if (step.Do == StepAction.Build && (civ ?? CivBonuses.None).Requires(entity) is { } required && !built.Contains(required))
            warn(id, $"Para construir {StepText.Name(step.What!)} hace falta {StepText.Name(required)} terminado, y la BO no lo construye antes de este paso.");

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
            if (AgeUpProgress(step.What, built) < 2)
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
        if (step.Assign is null)
            error(id, "Falta 'assign': ¿a qué recurso van los aldeanos?");
        if (step.From is not null && step.From == step.Assign)
            error(id, $"'from' y 'assign' son el mismo recurso ({step.From}).");
    }

    /// <summary>
    /// D12: los objetivos del modo asistido. Que la civ no pueda hacer algo no es un error: lo dice el generador como
    /// problema de ese objetivo.
    /// </summary>
    /// <summary>Solo las reglas de <c>assisted</c> (D12): con errores no se genera (la BO todavía no tiene pasos).</summary>
    public ValidationResult ValidateAssistedPlan(BuildOrder bo)
    {
        var issues = new List<ValidationIssue>();
        ValidateAssisted(bo.Assisted, (step, message) => issues.Add(new(Severity.Error, step, message)));
        return new ValidationResult(issues);
    }

    private void ValidateAssisted(AssistedPlan? assisted, Action<string?, string> error)
    {
        if (assisted is null)
            return;
        if (assisted.Villagers.Until is < 1 or > MaxPopulation)
            error(null, $"assisted.villagers.until tiene que estar entre 1 y {MaxPopulation} (es {assisted.Villagers.Until}).");
        if (assisted.Opening.Sheep is < 0 or > 20)
            error(null, $"assisted.opening.sheep tiene que estar entre 0 y 20 (es {assisted.Opening.Sheep}).");
        if (assisted.Opening.Berries is not ("auto" or "no"))
            error(null, $"assisted.opening.berries tiene que ser \"auto\" o \"no\" (es \"{assisted.Opening.Berries}\").");

        for (var i = 0; i < assisted.Goals.Count; i++)
        {
            var goal = assisted.Goals[i];
            var where = $"assisted.goals[{i}]";
            var kinds = new[] { goal.Age is not null, goal.Unit is not null, goal.TownCenters is not null, goal.Tech is not null }.Count(k => k);
            if (kinds != 1)
            {
                error(null, $"{where}: tiene que tener exactamente uno de 'age', 'unit', 'town_centers' o 'tech' (tiene {kinds}).");
                continue;
            }
            if (goal.Age is { } ageTech && !AgeTechs.ContainsKey(ageTech))
                error(null, $"{where}: '{ageTech}' no es una edad (feudal_age, castle_age o imperial_age).");
            if (goal.AtPop is < 1 or > MaxPopulation)
                error(null, $"{where}: 'at_pop' tiene que estar entre 1 y {MaxPopulation} (es {goal.AtPop}).");
            if (goal.Unit is { } unit)
            {
                if (!gameData.TryGet(unit, out var entity) || entity.Kind != EntityKind.Unit)
                    error(null, $"{where}: '{unit}' no es una unidad conocida.");
                if (goal.Count is not { } count || count < 1 || count > MaxCount)
                    error(null, $"{where}: 'count' tiene que estar entre 1 y {MaxCount}.");
                if (goal.Buildings is < 1 or > 10)
                    error(null, $"{where}: 'buildings' tiene que estar entre 1 y 10 (es {goal.Buildings}).");
            }
            if (goal.TownCenters is < 2 or > 10)
                error(null, $"{where}: 'town_centers' tiene que estar entre 2 y 10 (es {goal.TownCenters}).");
            if (goal.Tech is { } tech && (!gameData.TryGet(tech, out var techEntity) || techEntity.Kind != EntityKind.Tech))
                error(null, $"{where}: '{tech}' no es una tecnología conocida.");
        }
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
