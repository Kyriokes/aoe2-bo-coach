using BoCoach.Core.Data;

namespace BoCoach.Core.BuildOrders;

public enum StepAction
{
    /// <summary>Entrenar unidades (<see cref="Step.What"/> es una unidad).</summary>
    Train,

    /// <summary>Construir edificios (<see cref="Step.What"/> es un edificio).</summary>
    Build,

    /// <summary>Investigar una tecnología o subir de edad.</summary>
    Research,

    /// <summary>Mover aldeanos de <see cref="Step.From"/> a <see cref="Step.Assign"/>.</summary>
    Move,
}

/// <summary>
/// Una build order. Es el mismo formato en la app, en los archivos locales y en la web.
/// El costo y el tiempo de cada paso no se escriben: salen de <see cref="GameData"/>.
/// </summary>
public sealed record BuildOrder
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Civ { get; init; } = "any";
    public string? Author { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<Step> Steps { get; init; } = [];
    public Targets? Targets { get; init; }
}

public sealed record Step
{
    public string Id { get; init; } = "";
    public StepAction Do { get; init; }

    /// <summary>Clave de entidad de game_data. No se usa en <see cref="StepAction.Move"/>.</summary>
    public string? What { get; init; }

    public int Count { get; init; } = 1;

    /// <summary>
    /// Train de aldeanos: a dónde va cada aldeano nuevo. Build: a dónde van los constructores al terminar.
    /// Move: destino.
    /// </summary>
    public GatherSource? Assign { get; init; }

    /// <summary>Move: de dónde salen los aldeanos. Build: de dónde salen los constructores (null = aldeanos nuevos o iniciales).</summary>
    public GatherSource? From { get; init; }

    /// <summary>Ids de pasos que tienen que estar hechos antes.</summary>
    public IReadOnlyList<string> After { get; init; } = [];

    /// <summary>Hacer el paso cuando la población llega a este número.</summary>
    public int? AtPop { get; init; }

    /// <summary>Hacer el paso a este segundo de partida.</summary>
    public int? AtTime { get; init; }

    /// <summary>Id de evento del log (data/events.json) que confirma el paso.</summary>
    public string? ConfirmBy { get; init; }

    public string? Note { get; init; }
}

public sealed record Targets
{
    /// <summary>Segundo de partida en que se hace click en Feudal.</summary>
    public int? FeudalClickTime { get; init; }

    /// <summary>Segundo de partida en que se hace click en Castillos.</summary>
    public int? CastleClickTime { get; init; }

    public VillagerDistribution? VillagersAtFeudal { get; init; }
    public VillagerDistribution? VillagersAtCastle { get; init; }
}

public sealed record VillagerDistribution(int Food, int Wood, int Gold, int Stone)
{
    public int Total => Food + Wood + Gold + Stone;
}
