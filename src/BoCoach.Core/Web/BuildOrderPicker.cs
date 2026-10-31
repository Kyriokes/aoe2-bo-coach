using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;

namespace BoCoach.Core.Web;

/// <summary>De dónde saca la app sus BO: la lista de la web y lo guardado (lo implementa <see cref="BuildOrderCache"/>).</summary>
public interface IBuildOrderSource
{
    WebBuildOrderList List { get; }
    BuildOrder? Saved(string code);
    IEnumerable<BuildOrder> AllSaved();
    Task<BuildOrder> GetAsync(string code, CancellationToken cancel = default);
}

/// <summary>Cómo se elige la BO del coach: la sugerida en cada partida, o la que el jugador eligió de su lista.</summary>
public enum BuildOrderMode
{
    Auto,
    Manual,
}

/// <summary>
/// Qué BO usa el coach (parte 4): la de más puntaje de tu lista para la partida de la pantalla de carga, bajada si
/// hace falta; sin internet, la mejor de las guardadas; sin ninguna que sirva, <see cref="BuildOrder.None"/>.
/// </summary>
public sealed class BuildOrderPicker(IBuildOrderSource source, CivStyles styles)
{
    /// <summary>Hasta qué segundo de partida se puede cambiar de BO (la pantalla de carga es el segundo 0).</summary>
    public const double SwitchSeconds = 60;

    public static bool CanSwitchAt(double gameTime) => gameTime <= SwitchSeconds;

    /// <summary>La última usada, si está guardada.</summary>
    public BuildOrder? Current(string? lastCode) => lastCode is { Length: > 0 } code ? source.Saved(code) : null;

    /// <summary>La de más puntaje de la lista para esa partida, sin bajarla.</summary>
    public SummarySuggestion? Best(MatchSetup match, MatchupStats? matchups) =>
        BuildOrderSuggester.Suggest(source.List.Items.Select(i => i.Summary), match, styles, matchups);

    /// <summary>La BO para la partida: la mejor de la lista; si no se puede bajar, la mejor guardada; si no, ninguna.</summary>
    public async Task<BuildOrder> ForMatchAsync(MatchSetup match, MatchupStats? matchups, CancellationToken cancel)
    {
        if (Best(match, matchups) is { } best)
        {
            try
            {
                return await source.GetAsync(best.Summary.Key, cancel);
            }
            catch (WebLibraryException)
            {
                // Sin internet: lo guardado.
            }
        }
        return BuildOrderSuggester.Suggest(source.AllSaved(), match, styles, matchups)?.BuildOrder ?? BuildOrder.None;
    }
}
