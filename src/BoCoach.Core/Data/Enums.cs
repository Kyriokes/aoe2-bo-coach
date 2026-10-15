namespace BoCoach.Core.Data;

public enum Resource
{
    Food,
    Wood,
    Stone,
    Gold,
}

public enum EntityKind
{
    Unit,
    Building,
    Tech,
}

public enum Age
{
    Dark,
    Feudal,
    Castle,
    Imperial,
}

/// <summary>Dónde trabaja un aldeano. Varias fuentes producen el mismo recurso.</summary>
public enum GatherSource
{
    Sheep,
    Hunt,
    Berries,
    Fish,
    Farm,
    Wood,
    Gold,
    Stone,
}

public static class GatherSourceExtensions
{
    public static Resource Resource(this GatherSource source) => source switch
    {
        GatherSource.Sheep or GatherSource.Hunt or GatherSource.Berries or GatherSource.Fish or GatherSource.Farm
            => Data.Resource.Food,
        GatherSource.Wood => Data.Resource.Wood,
        GatherSource.Gold => Data.Resource.Gold,
        GatherSource.Stone => Data.Resource.Stone,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
    };
}
