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
}
