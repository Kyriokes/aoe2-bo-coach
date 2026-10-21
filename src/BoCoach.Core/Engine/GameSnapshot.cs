using BoCoach.Core.Data;

namespace BoCoach.Core.Engine;

/// <summary>Un evento del log del juego ya reconocido (id de data/events.json).</summary>
/// <param name="Time">Segundo de partida en que se vio.</param>
/// <param name="Synthetic">True si no salió del log sino de otra señal (por ejemplo, la población subió).</param>
public sealed record GameEvent(string EventId, double Time, bool Synthetic = false);

/// <summary>Lo leído de la pantalla en un instante. Es lo único que el motor sabe del juego.</summary>
/// <param name="Time">Reloj de la partida, en segundos.</param>
/// <param name="Events">Eventos nuevos desde el snapshot anterior (Vision ya los deduplicó).</param>
public sealed record GameSnapshot(
    double Time,
    ResourceAmounts Stock,
    int Population,
    int PopulationCap,
    IReadOnlyList<GameEvent> Events)
{
    public GameSnapshot(double time, ResourceAmounts stock, int population, int populationCap, params GameEvent[] events)
        : this(time, stock, population, populationCap, (IReadOnlyList<GameEvent>)events)
    {
    }

    /// <summary>
    /// Total de aldeanos que muestra el HUD (número azul debajo de la población), o null si no se leyó. Cuando se
    /// lee, los aldeanos nuevos salen de este número y no del log, que muestra varios aldeanos seguidos como uno.
    /// </summary>
    public int? Villagers { get; init; }

    /// <summary>
    /// Aldeanos en cada recurso según el HUD (los números chicos de cada recurso; se leen con el mod BoCoach HUD), o
    /// null si no se leyeron los cuatro. No incluye a los que construyen ni a los inactivos.
    /// </summary>
    public ResourceAmounts? VillagersOn { get; init; }

    /// <summary>
    /// Lo que hay en la cola de producción del juego, o null si no se leyó. Vacía = no se está produciendo nada.
    /// </summary>
    public IReadOnlyList<QueueItem>? Queue { get; init; }

    /// <summary>El panel de puntajes, si se leyó en esta captura (se lee cada 2 s), o null.</summary>
    public IReadOnlyList<PlayerScore>? Scores { get; init; }

    /// <summary>Aldeanos inactivos según el HUD, o null si no se leyó.</summary>
    public int? IdleVillagers { get; init; }
}

/// <summary>Un renglón del panel de puntajes: el jugador (por su color), su puntaje y si sigue en la Edad Oscura.</summary>
public sealed record PlayerScore(int Slot, int Score, bool DarkAge);

/// <summary>
/// Un ícono de la cola de producción: la clave de game_data ("housed" = esperando población, "?" = ícono desconocido)
/// y si espera detrás de otra cosa del mismo edificio (el juego lo pinta de amarillo). La cola tiene un ícono por
/// edificio trabajando más los que esperan, así que dos íconos iguales que no esperan son dos edificios.
/// </summary>
public sealed record QueueItem(string Key, bool Waiting = false)
{
    public override string ToString() => Waiting ? $"{Key}…" : Key;
}
