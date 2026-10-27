namespace BoCoach.Core.BuildOrders;

/// <summary>
/// D12: los objetivos del modo asistido, de los que sale la BO (el campo <c>assisted</c>). Los pasos generados también
/// se guardan en la BO, así la app, el coach y las versiones viejas la usan como cualquier otra. Las propiedades son
/// <c>set</c> (ver <see cref="BuildOrder"/>).
/// </summary>
public sealed record AssistedPlan
{
    public AssistedOpening Opening { get; set; } = new();

    /// <summary>Aldeanos sin parar hasta un total.</summary>
    public AssistedVillagers Villagers { get; set; } = new();

    /// <summary>Mejoras económicas automáticas (sub-paso d).</summary>
    public bool EcoUpgrades { get; set; }

    /// <summary>Los objetivos en orden de prioridad: lo de arriba tiene prioridad sobre los recursos.</summary>
    public IReadOnlyList<AssistedGoal> Goals { get; set; } = [];
}

/// <summary>Cómo arranca la Edad Oscura.</summary>
public sealed record AssistedOpening
{
    /// <summary>Las 2 casas con los aldeanos iniciales (se ignora en las civs sin casas).</summary>
    public bool Houses { get; set; } = true;

    /// <summary>Cuántos aldeanos van a las ovejas antes de abrir otro recurso, contando los iniciales.</summary>
    public int Sheep { get; set; } = 6;

    /// <summary>Cazar los jabalíes después de las ovejas.</summary>
    public bool Boar { get; set; } = true;

    /// <summary>"auto": las bayas (con molino) cuando la comida lo pide; "no": nunca.</summary>
    public string Berries { get; set; } = "auto";
}

public sealed record AssistedVillagers
{
    public int Until { get; set; } = 120;
}

/// <summary>
/// Un objetivo: exactamente uno de <see cref="Age"/>, <see cref="Unit"/>, <see cref="TownCenters"/> o
/// <see cref="Tech"/> (un registro plano: los serializadores generados al compilar se llevan mal con tipos que heredan).
/// </summary>
public sealed record AssistedGoal
{
    /// <summary>"feudal_age", "castle_age" o "imperial_age".</summary>
    public string? Age { get; set; }

    /// <summary>Con <see cref="Age"/>: la población del clic (null = lo antes posible).</summary>
    public int? AtPop { get; set; }

    /// <summary>Una unidad, con <see cref="Count"/> y <see cref="Buildings"/>.</summary>
    public string? Unit { get; set; }

    public int? Count { get; set; }

    /// <summary>Cuántos edificios producen la unidad a la vez.</summary>
    public int? Buildings { get; set; }

    /// <summary>Cuántos centros urbanos en total.</summary>
    public int? TownCenters { get; set; }

    /// <summary>Una tecnología a pedido.</summary>
    public string? Tech { get; set; }
}
