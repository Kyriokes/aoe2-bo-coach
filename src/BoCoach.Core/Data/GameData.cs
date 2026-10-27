namespace BoCoach.Core.Data;

/// <summary>Una unidad, edificio o tecnología del juego, con los valores base (sin bonos de civ).</summary>
/// <param name="Time">Segundos de entrenamiento, construcción (un aldeano) o investigación.</param>
/// <param name="MadeIn">Clave del edificio donde se produce; null para edificios.</param>
/// <param name="Pop">Población que ocupa (unidades).</param>
/// <param name="PopBonus">Población que suma al terminarse (casa, TC, castillo).</param>
public sealed record Entity(
    string Key,
    EntityKind Kind,
    int DatId,
    string DatName,
    ResourceAmounts Cost,
    int Time,
    Age Age,
    string? MadeIn,
    int Pop,
    int PopBonus)
{
    /// <summary>Lo que hace (si es una tecnología) con la recolección de cada fuente: hachas, carretilla, minería.</summary>
    public IReadOnlyDictionary<GatherSource, GatherEffect> Gather { get; init; } = new Dictionary<GatherSource, GatherEffect>();

    /// <summary>
    /// Edificios por los que vale este a la vez (el Settlement de los incas: campamento maderero, campamento minero,
    /// molino y casa): confirma sus pasos, cubre los requisitos de edad y produce lo que ellos producen.
    /// </summary>
    public IReadOnlyList<string> CountsAs { get; init; } = [];

    /// <summary>
    /// El edificio que tiene que estar terminado antes de poder construir este (la galería y el establo, el cuartel; el
    /// taller de asedio, la herrería; el mercado y la granja, el molino), según los árboles del juego. Vale también lo
    /// que cuenta como él (<see cref="CountsAs"/>).
    /// </summary>
    public string? Requires { get; init; }

    /// <summary>Lo que suma (si es una tecnología) a la comida de cada granja nueva o resembrada: collera +75, arado pesado +125.</summary>
    public double FarmFood { get; init; }
}

/// <summary>
/// Efecto sobre los aldeanos de una fuente: el trabajo y la velocidad se multiplican; la carga se multiplica y después
/// se suma (carretilla: carga ×1,27 y velocidad ×1,1; arado pesado: +1 de carga a los granjeros).
/// </summary>
public sealed record GatherEffect(double Work = 1, double CarryMultiplier = 1, double CarryAdd = 0, double Speed = 1);

/// <summary>Datos del juego de un parche, generados por tools/extractor desde el .dat.</summary>
public sealed class GameData
{
    /// <summary>Carga por viaje del aldeano de cada fuente cuando el .dat no la dice.</summary>
    public const double DefaultCarry = 10;

    public GameData(string datVersion, IReadOnlyDictionary<string, Entity> entities, IReadOnlyDictionary<GatherSource, double> gatherRates,
        IReadOnlyDictionary<string, CivBonuses>? civs = null, IReadOnlyDictionary<GatherSource, double>? gatherCarry = null,
        IReadOnlyDictionary<GatherSource, double>? foodPerObject = null)
    {
        DatVersion = datVersion;
        Entities = entities;
        GatherRates = gatherRates;
        Civs = civs ?? new Dictionary<string, CivBonuses>();
        GatherCarry = gatherCarry ?? new Dictionary<GatherSource, double>();
        FoodPerObject = foodPerObject ?? new Dictionary<GatherSource, double>();
    }

    public string DatVersion { get; }

    public IReadOnlyDictionary<string, Entity> Entities { get; }

    /// <summary>Tasa de trabajo del .dat por fuente (recurso por segundo, sin contar caminatas).</summary>
    public IReadOnlyDictionary<GatherSource, double> GatherRates { get; }

    /// <summary>Cuánto lleva por viaje el aldeano de cada fuente (10; el cazador, 35).</summary>
    public IReadOnlyDictionary<GatherSource, double> GatherCarry { get; }

    public double Carry(GatherSource source) => GatherCarry.GetValueOrDefault(source, DefaultCarry);

    /// <summary>
    /// D12: la comida de una oveja, un jabalí, un arbusto de bayas y una granja sin mejoras (100, 340, 125 y 175), para
    /// la comida finita del modo asistido. Vacío si el archivo es de antes del extractor que la exporta.
    /// </summary>
    public IReadOnlyDictionary<GatherSource, double> FoodPerObject { get; }

    public Entity Get(string key) =>
        Entities.TryGetValue(key, out var entity)
            ? entity
            : throw new KeyNotFoundException($"La entidad '{key}' no existe en game_data {DatVersion}.");

    public bool TryGet(string key, out Entity entity) => Entities.TryGetValue(key, out entity!);

    /// <summary>Bonificaciones de cada civ por su clave ("britons", "franks"...).</summary>
    public IReadOnlyDictionary<string, CivBonuses> Civs { get; }

    /// <summary>Las bonificaciones de la civ de una BO; "any", vacío o una civ desconocida = sin bonificaciones.</summary>
    public CivBonuses Civ(string? key) =>
        key is not null && Civs.TryGetValue(key.Trim().ToLowerInvariant(), out var civ) ? civ : CivBonuses.None;

    public bool IsKnownCiv(string? key) => key is not null && Civs.ContainsKey(key.Trim().ToLowerInvariant());

    /// <summary>
    /// Si la civ puede hacer esa entidad: no está entre las que le faltan y tampoco el edificio donde se hace (las
    /// unidades del establo no figuran en el árbol de una civ sin establo). "any" puede todo.
    /// </summary>
    public bool CanMake(string? civ, string key)
    {
        var bonuses = Civ(civ);
        if (bonuses.Unavailable.Contains(key))
            return false;
        return !(TryGet(key, out var entity) && entity.MadeIn is { } producer && bonuses.Unavailable.Contains(producer));
    }

    /// <summary>
    /// La clave de una civ por cualquiera de sus nombres ("Mayans", "Inca", "Mayas"), sin mayúsculas, espacios ni la
    /// "s" del plural; null si no es ninguna.
    /// </summary>
    public string? CivByName(string? name)
    {
        var wanted = Flat(name);
        if (wanted.Length == 0)
            return null;
        return Civs.FirstOrDefault(c => new[] { c.Key }.Concat(c.Value.Names.Values).Any(n => Flat(n) == wanted)).Key;

        static string Flat(string? text) =>
            new string((text ?? "").ToLowerInvariant().Where(char.IsLetter).ToArray()).TrimEnd('s');
    }
}
