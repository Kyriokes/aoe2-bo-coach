using BoCoach.Core.Data;

namespace BoCoach.Core.Engine;

/// <summary>
/// D12: la comida que hay en el mapa para el Planner con la comida finita (el modo asistido). <see cref="Pools"/> es el
/// total de cada fuente que se agota (ovejas, caza y bayas); lo que no está ahí (madera, oro, piedra, pesca) no se
/// acaba. Las granjas van aparte: cada una tiene <see cref="FarmFood"/> más lo que suman las mejoras.
/// </summary>
public sealed record FoodSupply(IReadOnlyDictionary<GatherSource, double> Pools)
{
    /// <summary>Las fuentes que se cuentan en <see cref="Pools"/>.</summary>
    public static IReadOnlyList<GatherSource> Counted { get; } = [GatherSource.Sheep, GatherSource.Hunt, GatherSource.Berries];

    /// <summary>La comida de una granja sin mejoras; null = la de game_data (175).</summary>
    public double? FarmFood { get; init; }

    /// <summary>
    /// La comida del mapa: cuántos objetos tiene (<see cref="GameMap.Food"/>; sin mapa, los de Arabia; 0 si el mapa no
    /// tiene esa fuente) por la comida de cada uno (game_data).
    /// </summary>
    public static FoodSupply For(GameData data, GameMap? map)
    {
        if (Counted.Append(GatherSource.Farm).Any(s => !data.FoodPerObject.ContainsKey(s)))
            throw new InvalidOperationException("game_data no trae 'food_per_object': hay que correr el extractor.");
        return new FoodSupply(Counted.ToDictionary(s => s, s => Count(map, s) * data.FoodPerObject[s]));
    }

    private static int Count(GameMap? map, GatherSource source) =>
        map is null ? GameMap.StandardFood[source]
        : !map.Has(source) ? 0
        : map.Food.GetValueOrDefault(source, GameMap.StandardFood[source]);
}
