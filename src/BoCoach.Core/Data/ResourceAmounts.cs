namespace BoCoach.Core.Data;

/// <summary>Cantidades de los cuatro recursos. Se usa para costos, stock y tasas.</summary>
public readonly record struct ResourceAmounts(double Food, double Wood, double Stone, double Gold)
{
    public static readonly ResourceAmounts Zero = default;

    public double this[Resource resource] => resource switch
    {
        Resource.Food => Food,
        Resource.Wood => Wood,
        Resource.Stone => Stone,
        Resource.Gold => Gold,
        _ => throw new ArgumentOutOfRangeException(nameof(resource), resource, null),
    };

    public static ResourceAmounts Of(Resource resource, double amount) => Zero.With(resource, amount);

    public ResourceAmounts With(Resource resource, double amount) => resource switch
    {
        Resource.Food => this with { Food = amount },
        Resource.Wood => this with { Wood = amount },
        Resource.Stone => this with { Stone = amount },
        Resource.Gold => this with { Gold = amount },
        _ => throw new ArgumentOutOfRangeException(nameof(resource), resource, null),
    };

    public static ResourceAmounts operator +(ResourceAmounts a, ResourceAmounts b) =>
        new(a.Food + b.Food, a.Wood + b.Wood, a.Stone + b.Stone, a.Gold + b.Gold);

    public static ResourceAmounts operator -(ResourceAmounts a, ResourceAmounts b) =>
        new(a.Food - b.Food, a.Wood - b.Wood, a.Stone - b.Stone, a.Gold - b.Gold);

    public static ResourceAmounts operator *(ResourceAmounts a, double k) =>
        new(a.Food * k, a.Wood * k, a.Stone * k, a.Gold * k);

    /// <summary>True si alcanza para pagar <paramref name="cost"/>.</summary>
    public bool Covers(ResourceAmounts cost) =>
        Food >= cost.Food && Wood >= cost.Wood && Stone >= cost.Stone && Gold >= cost.Gold;

    public override string ToString() => $"F{Food:0} W{Wood:0} S{Stone:0} G{Gold:0}";
}
