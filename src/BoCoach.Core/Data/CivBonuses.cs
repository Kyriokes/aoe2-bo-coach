namespace BoCoach.Core.Data;

/// <summary>Cómo arranca una civ una partida estándar, respecto del arranque común (3 aldeanos, 200/200/200/100).</summary>
/// <param name="Resources">Recursos de más o de menos (chinos: -200 de comida y -50 de madera).</param>
/// <param name="ExtraVillagers">Aldeanos de más (chinos +3, mayas +1).</param>
/// <param name="NoHouses">No necesita casas (hunos): la población solo tiene el tope máximo.</param>
public sealed record CivStart(ResourceAmounts Resources, int ExtraVillagers, bool NoHouses)
{
    public static CivStart None { get; } = new(ResourceAmounts.Zero, 0, false);
}

/// <summary>
/// Las bonificaciones de una civilización por edad, sacadas del .dat por tools/extractor: velocidad de recolección y
/// de construcción, costo y tiempo de cada entidad, velocidad de producción de cada edificio y población que da.
/// Lo que no figura no cambia. <see cref="None"/> es la civ sin bonificaciones ("any").
/// </summary>
public sealed class CivBonuses
{
    private readonly IReadOnlyDictionary<GatherSource, double[]> _gather;
    private readonly double[]? _buildRate;
    private readonly IReadOnlyDictionary<string, ResourceAmounts[]> _costs;
    private readonly IReadOnlyDictionary<string, double[]> _times;
    private readonly IReadOnlyDictionary<string, double[]> _workRate;
    private readonly IReadOnlyDictionary<string, int[]> _popBonus;
    private readonly IReadOnlyDictionary<GatherSource, double[]> _carry;
    private readonly double[]? _villagerSpeed;
    private readonly IReadOnlyDictionary<Age, ResourceAmounts> _ageResources;

    public CivBonuses(
        string key,
        string datName,
        IReadOnlyDictionary<GatherSource, double[]> gather,
        double[]? buildRate,
        IReadOnlyDictionary<string, ResourceAmounts[]> costs,
        IReadOnlyDictionary<string, double[]> times,
        IReadOnlyDictionary<string, double[]> workRate,
        IReadOnlyDictionary<string, int[]> popBonus,
        CivStart start,
        IReadOnlyDictionary<GatherSource, double[]>? carry = null,
        double[]? villagerSpeed = null,
        IReadOnlyDictionary<Age, ResourceAmounts>? ageResources = null)
    {
        Key = key;
        DatName = datName;
        _gather = gather;
        _buildRate = buildRate;
        _costs = costs;
        _times = times;
        _workRate = workRate;
        _popBonus = popBonus;
        Start = start;
        _carry = carry ?? new Dictionary<GatherSource, double[]>();
        _villagerSpeed = villagerSpeed;
        _ageResources = ageResources ?? new Dictionary<Age, ResourceAmounts>();
    }

    public static CivBonuses None { get; } = new("any", "", new Dictionary<GatherSource, double[]>(), null,
        new Dictionary<string, ResourceAmounts[]>(), new Dictionary<string, double[]>(), new Dictionary<string, double[]>(),
        new Dictionary<string, int[]>(), CivStart.None);

    public string Key { get; }

    /// <summary>Nombre interno de la civ en el .dat ("British", "French"...).</summary>
    public string DatName { get; }

    /// <summary>
    /// Nombre que muestra el juego (en la pantalla de carga), por idioma del juego: "en", "es" (España) y "mx"
    /// (Latinoamérica). Los incas son "Inca" en inglés y en "es", e "Incas" en "mx".
    /// </summary>
    public IReadOnlyDictionary<string, string> Names { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Lo que la civ no tiene según su árbol de tecnologías (claves de game_data): los hunos no tienen casas, los
    /// mesoamericanos establo. Las unidades de un edificio que falta las descarta <see cref="GameData.CanMake"/>.
    /// </summary>
    public IReadOnlySet<string> Unavailable { get; init; } = new HashSet<string>();

    public CivStart Start { get; }

    public bool IsNone => ReferenceEquals(this, None);

    /// <summary>Multiplicador de la velocidad de recolección de una fuente (británicos: pastores ×1,25).</summary>
    public double GatherMultiplier(GatherSource source, Age age) => _gather.TryGetValue(source, out var v) ? v[(int)age] : 1;

    /// <summary>Multiplicador de la velocidad de construcción de los aldeanos (españoles ×1,3).</summary>
    public double BuildRate(Age age) => _buildRate?[(int)age] ?? 1;

    public ResourceAmounts Cost(Entity entity, Age age) => _costs.TryGetValue(entity.Key, out var v) ? v[(int)age] : entity.Cost;

    /// <summary>Segundos de entrenamiento, construcción o investigación (sin contar la velocidad del edificio).</summary>
    public double Time(Entity entity, Age age) => _times.TryGetValue(entity.Key, out var v) ? v[(int)age] : entity.Time;

    /// <summary>Multiplicador de la velocidad con que un edificio produce o investiga (persas: centro urbano).</summary>
    public double WorkRate(string? building, Age age) =>
        building is not null && _workRate.TryGetValue(building, out var v) ? v[(int)age] : 1;

    /// <summary>Población que da un edificio (chinos: centro urbano 15; incas: casas 10).</summary>
    public int PopBonus(Entity entity, Age age) => _popBonus.TryGetValue(entity.Key, out var v) ? v[(int)age] : entity.PopBonus;

    /// <summary>Carga por viaje de los aldeanos de una fuente sin contar tecnologías, o null si es la de siempre
    /// (aztecas +3 en todo; dravidianos +15 en la pesca).</summary>
    public double? Carry(GatherSource source, Age age) => _carry.TryGetValue(source, out var v) ? v[(int)age] : null;

    /// <summary>Multiplicador de la velocidad de los aldeanos (bereberes ×1,05 en Oscura y ×1,1 después).</summary>
    public double VillagerSpeed(Age age) => _villagerSpeed?[(int)age] ?? 1;

    /// <summary>Lo que se recibe al llegar a una edad (dravidianos +200 de madera; etíopes +100 de comida y de oro).</summary>
    public ResourceAmounts AgeResources(Age age) => _ageResources.GetValueOrDefault(age, ResourceAmounts.Zero);
}
