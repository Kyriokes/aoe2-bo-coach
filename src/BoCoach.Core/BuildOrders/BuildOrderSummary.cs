namespace BoCoach.Core.BuildOrders;

/// <summary>
/// Lo que hace falta de una BO para sugerirla sin sus pasos: la lista de la web ("mis BO") trae esto y la app baja
/// la BO entera recién cuando la va a usar.
/// </summary>
/// <param name="Key">El código de la web, o el id de la BO si sale de una BO entera (<see cref="Of"/>).</param>
/// <param name="Style">El estilo por sus pasos (<see cref="BuildOrderStyle"/>), o null.</param>
public sealed record BuildOrderSummary(string Key, string Name, string Civ, string? Map, IReadOnlyList<string> Tags, string? Style)
{
    public static BuildOrderSummary Of(BuildOrder bo) => new(bo.Id, bo.Name, bo.Civ, bo.Map, bo.Tags, BuildOrderStyle.Of(bo));
}

/// <summary>Un resumen sugerido, por qué (para el overlay) y sus puntos.</summary>
public sealed record SummarySuggestion(BuildOrderSummary Summary, string Reason, double Score);
