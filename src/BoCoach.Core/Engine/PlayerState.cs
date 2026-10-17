using BoCoach.Core.Data;

namespace BoCoach.Core.Engine;

/// <summary>
/// Lo acumulado de la partida a partir de los snapshots: último stock, población y todos los eventos vistos.
/// Los aldeanos nuevos salen, en este orden de preferencia:
/// <list type="number">
/// <item>Del total de aldeanos del HUD (<see cref="GameSnapshot.Villagers"/>): cada vez que sube es un aldeano
/// (evento sintético) y los "Villager Created" del log se ignoran, porque varios seguidos se ven como uno.</item>
/// <item>Si el HUD no lo muestra: de la población. Si sube y el log no mostró qué unidad salió, se asume un
/// aldeano (evento sintético); si el evento real llega después, se descarta para no contarlo dos veces.</item>
/// </list>
/// </summary>
public sealed class PlayerState
{
    /// <summary>Margen para emparejar un evento real con uno sintético ya creado (el log puede aparecer con atraso).</summary>
    public const double MatchWindowSeconds = 8;

    /// <summary>Aldeanos con los que arranca la partida.</summary>
    public const int StartingVillagers = 3;

    private readonly List<GameEvent> _events = [];
    private int _lastPopulation;
    private int? _lastVillagers;

    public PlayerState(int startingPopulation = 4, int startingPopulationCap = 5)
    {
        Population = _lastPopulation = startingPopulation;
        PopulationCap = startingPopulationCap;
    }

    public double Time { get; private set; }
    public ResourceAmounts Stock { get; private set; } = PlanStart.Standard.Stock;
    public int Population { get; private set; }
    public int PopulationCap { get; private set; }

    /// <summary>Último total de aldeanos leído del HUD, o null si nunca se leyó.</summary>
    public int? Villagers => _lastVillagers;

    /// <summary>Últimos aldeanos por recurso leídos del HUD (con el mod), o null si nunca se leyeron.</summary>
    public ResourceAmounts? VillagersOn { get; private set; }

    /// <summary>Eventos en orden de llegada, incluidos los sintéticos.</summary>
    public IReadOnlyList<GameEvent> Events => _events;

    /// <summary>Cuántos eventos nuevos se agregaron en el último <see cref="Apply"/>.</summary>
    public int LastAdded { get; private set; }

    public void Apply(GameSnapshot snapshot)
    {
        var before = _events.Count;
        Time = snapshot.Time;
        Stock = snapshot.Stock;
        PopulationCap = snapshot.PopulationCap;
        VillagersOn = snapshot.VillagersOn ?? VillagersOn;

        if (snapshot.Villagers is not null || _lastVillagers is not null)
            ApplyVillagersFromHud(snapshot);
        else
            ApplyVillagersFromPopulation(snapshot);

        Population = _lastPopulation = snapshot.Population;
        LastAdded = _events.Count - before;
    }

    public int Count(string eventId) => _events.Count(e => e.EventId == eventId);

    /// <summary>
    /// Con el total del HUD a la vista (o visto alguna vez), los aldeanos salen de ese número. La primera lectura
    /// se compara con los aldeanos iniciales, así un coach que arranca a mitad de partida se pone al día.
    /// </summary>
    private void ApplyVillagersFromHud(GameSnapshot snapshot)
    {
        foreach (var e in snapshot.Events)
        {
            if (e.EventId != EventConventions.VillagerCreated)
                _events.Add(e);
        }

        if (snapshot.Villagers is not { } villagers)
            return;
        var growth = villagers - (_lastVillagers ?? StartingVillagers);
        for (var i = 0; i < growth; i++)
            _events.Add(new GameEvent(EventConventions.VillagerCreated, snapshot.Time, Synthetic: true));
        _lastVillagers = villagers;
    }

    private void ApplyVillagersFromPopulation(GameSnapshot snapshot)
    {
        // Las unidades de este snapshot explican primero la población que subió ahora; las que sobran
        // son eventos atrasados de una suba anterior, que ya se había contado como aldeano sintético.
        var growth = Math.Max(0, snapshot.Population - _lastPopulation);
        var unitsSeen = 0;
        foreach (var e in snapshot.Events)
        {
            if (EventConventions.IsUnitCreated(e.EventId) && ++unitsSeen > growth && TryReplaceSynthetic(e))
                continue;
            _events.Add(e);
        }

        for (var i = unitsSeen; i < growth; i++)
            _events.Add(new GameEvent(EventConventions.VillagerCreated, snapshot.Time, Synthetic: true));
    }

    /// <summary>
    /// Un evento real de unidad que llega después de que la población ya subió: si hay un aldeano sintético
    /// reciente, el real lo reemplaza (mismo lugar en la lista) en vez de sumarse.
    /// </summary>
    private bool TryReplaceSynthetic(GameEvent real)
    {
        var index = _events.FindLastIndex(e => e.Synthetic && real.Time - e.Time <= MatchWindowSeconds);
        if (index < 0)
            return false;
        _events[index] = real with { Time = _events[index].Time };
        return true;
    }
}
