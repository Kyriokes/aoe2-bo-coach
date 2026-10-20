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

    /// <summary>
    /// Cómo cierra la BO (D2): qué se produce siempre y a qué edad se apunta cuando se terminan los pasos. Opcional y
    /// sin subir <see cref="SchemaVersion"/>: las versiones viejas ignoran el campo. Sin ella, se deduce
    /// (<see cref="Engine.Posture.Effective"/>).
    /// </summary>
    public ClosingPosture? Posture { get; set; }

    /// <summary>
    /// Ramas (D3): pasos que se agregan cuando aparece una señal de la partida (el rival hizo clic en una edad, te
    /// atacan, perdiste aldeanos). Opcional y sin subir <see cref="SchemaVersion"/>, como la postura.
    /// </summary>
    public IReadOnlyList<Branch>? Branches { get; set; }
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

/// <summary>Una rama de la BO: si aparece la señal <see cref="On"/> (antes de <see cref="Before"/>), estos pasos.</summary>
public sealed record Branch
{
    /// <summary>Las señales que pueden disparar una rama, con su texto para el editor y el overlay.</summary>
    public static readonly IReadOnlyDictionary<string, string> Signals = new Dictionary<string, string>
    {
        ["rival_feudal_click"] = "Si el rival hace clic en Feudal",
        ["rival_castle_click"] = "Si el rival hace clic en Castillos",
        ["rival_imperial_click"] = "Si el rival hace clic en Imperial",
        ["under_attack"] = "Si te atacan",
        ["villagers_lost"] = "Si perdés aldeanos",
    };

    /// <summary>La señal (<see cref="Signals"/>).</summary>
    public string On { get; set; } = "";

    /// <summary>Solo si la señal llega antes de este segundo de partida (null = en cualquier momento).</summary>
    public int? Before { get; set; }

    /// <summary>Qué hacer y por qué, para el overlay ("Lanceros contra los exploradores").</summary>
    public string? Note { get; set; }

    public IReadOnlyList<Step> Steps { get; set; } = [];
}

/// <summary>La postura de cierre de una BO.</summary>
public sealed record ClosingPosture
{
    /// <summary>Unidades siempre en cola ("villager", "scout_cavalry"), cada una en todos los edificios que la producen.</summary>
    public IReadOnlyList<string> Queue { get; set; } = [];

    /// <summary>La edad a la que se apunta con lo que sobra ("castle_age"), o null.</summary>
    public string? NextAge { get; set; }
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
