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
/// Las propiedades son <c>set</c> y no <c>init</c>: con los serializadores generados al compilar (CoreJson), una
/// propiedad <c>init</c> que no viene en el JSON queda en null o 0 en vez de su valor por defecto (count = 1).
/// </summary>
public sealed record BuildOrder
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Civ { get; set; } = "any";
    public string? Author { get; set; }
    public string? Description { get; set; }
    public IReadOnlyList<string> Tags { get; set; } = [];
    public IReadOnlyList<Step> Steps { get; set; } = [];
    public Targets? Targets { get; set; }
}

public sealed record Step
{
    public string Id { get; set; } = "";
    public StepAction Do { get; set; }

    /// <summary>Clave de entidad de game_data. No se usa en <see cref="StepAction.Move"/>.</summary>
    public string? What { get; set; }

    public int Count { get; set; } = 1;

    /// <summary>
    /// Train de aldeanos: a dónde va cada aldeano nuevo. Build: a dónde van los constructores al terminar.
    /// Move: destino.
    /// </summary>
    public GatherSource? Assign { get; set; }

    /// <summary>Move: de dónde salen los aldeanos. Build: de dónde salen los constructores (null = aldeanos sin tarea, como los iniciales).</summary>
    public GatherSource? From { get; set; }

    /// <summary>
    /// Build: cuántos aldeanos construyen juntos (por defecto 1). En granjas cada aldeano hace la suya,
    /// así que por defecto son tantos como granjas.
    /// </summary>
    public int? Builders { get; set; }

    /// <summary>Ids de pasos que tienen que estar hechos antes.</summary>
    public IReadOnlyList<string> After { get; set; } = [];

    /// <summary>Hacer el paso cuando la población llega a este número.</summary>
    public int? AtPop { get; set; }

    /// <summary>Hacer el paso a este segundo de partida.</summary>
    public int? AtTime { get; set; }

    /// <summary>Id de evento del log (data/events.json) que confirma el paso.</summary>
    public string? ConfirmBy { get; set; }

    /// <summary>
    /// Paso mixto: los pasos seguidos con el mismo grupo son un solo paso con varias metas que se hacen a la vez
    /// (por ejemplo, el cuartel mientras siguen saliendo aldeanos a madera). Cada meta avanza por su cuenta y el paso
    /// termina cuando terminan todas. null = paso simple.
    /// </summary>
    public string? Group { get; set; }

    public string? Note { get; set; }
}

public sealed record Targets
{
    /// <summary>Segundo de partida en que se hace click en Feudal.</summary>
    public int? FeudalClickTime { get; set; }

    /// <summary>Segundo de partida en que se hace click en Castillos.</summary>
    public int? CastleClickTime { get; set; }

    public VillagerDistribution? VillagersAtFeudal { get; set; }
    public VillagerDistribution? VillagersAtCastle { get; set; }
}

public sealed record VillagerDistribution(int Food, int Wood, int Gold, int Stone)
{
    public int Total => Food + Wood + Gold + Stone;
}
