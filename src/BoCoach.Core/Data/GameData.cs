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
    int PopBonus);

/// <summary>Datos del juego de un parche, generados por tools/extractor desde el .dat.</summary>
public sealed class GameData
{
    public GameData(string datVersion, IReadOnlyDictionary<string, Entity> entities, IReadOnlyDictionary<GatherSource, double> gatherRates)
    {
        DatVersion = datVersion;
        Entities = entities;
        GatherRates = gatherRates;
    }

    public string DatVersion { get; }

    public IReadOnlyDictionary<string, Entity> Entities { get; }

    /// <summary>Tasa de trabajo del .dat por fuente (recurso por segundo, sin contar caminatas).</summary>
    public IReadOnlyDictionary<GatherSource, double> GatherRates { get; }

    public Entity Get(string key) =>
        Entities.TryGetValue(key, out var entity)
            ? entity
            : throw new KeyNotFoundException($"La entidad '{key}' no existe en game_data {DatVersion}.");

    public bool TryGet(string key, out Entity entity) => Entities.TryGetValue(key, out entity!);
}
