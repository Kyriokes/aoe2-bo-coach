using BoCoach.Core.Data;

namespace BoCoach.Core.Engine;

/// <summary>Lo que se pregunta a las estadísticas de cruces: tu civ, el rival (en 1v1), el mapa y tu Elo.</summary>
public sealed record MatchupQuery(string Civ, string? Rival, string? Map, int? Elo, bool Team);

/// <summary>
/// Los textos de las estadísticas de cruces (spec D10, 5.2 a 5.4): el renglón de la pantalla de carga, la sección
/// del informe y el porqué de la sugerencia. Siguen la regla de honestidad: "parejo" si el rango toca el 50 %, y nada
/// por duración si la franja más corta y la más larga no se distinguen del ruido. Los nombres los pone quien llama:
/// la app sabe nombrar las civs según el idioma del juego y los mapas salen de data/maps.json.
/// </summary>
public static class MatchupText
{
    /// <param name="civName">El nombre de la civ en minúsculas ("francos").</param>
    /// <param name="mapName">El nombre del mapa ("Arabia", "Nómada"), o null si no se conoce.</param>
    public static string? Loading(MatchupStats stats, MatchupQuery query, Func<string, string> civName, Func<string, string?> mapName)
    {
        if (Summary(stats, query, civName, mapName) is not { } resumen)
            return Missing(stats, query, civName);
        var texto = resumen.Headline + Estimated(resumen, query) + (resumen.General.N > 0 ? Duration(stats, query, resumen) : "");
        return stats.PreviousMixed ? texto + " (Con datos del parche anterior.)" : texto;
    }

    /// <summary>
    /// La sección "El cruce" del informe: el renglón general (sin la tendencia ni el aviso del parche) y el de la
    /// duración que tuvo la partida. El informe no sabe si ganaste: es contexto, no un veredicto.
    /// </summary>
    public static IReadOnlyList<string> Report(MatchupStats stats, MatchupQuery query, TimeSpan gameLength,
        Func<string, string> civName, Func<string, string?> mapName)
    {
        if (Summary(stats, query, civName, mapName) is not { } resumen)
            return [];
        var band = stats.DurationBand(gameLength);
        var cell = query.Team
            ? stats.Team(query.Civ, resumen.Map, resumen.EloBand, band)
            : stats.OneVsOne(query.Civ, query.Rival!, resumen.Map, resumen.EloBand, band);
        var duro = $"Duró {(int)gameLength.TotalMinutes} min: a esa duración, ";
        var segundo = query.Team
            ? $"{duro}los equipos con {civName(query.Civ)} ganan el {Range(cell!)}."
            : $"{duro}en este cruce ganás el {Range(cell!)}.";
        if (cell!.N == 0)
            segundo += " Estimado: pocas partidas así.";
        return [resumen.Headline + Estimated(resumen, query), segundo];
    }

    /// <summary>El porqué de la sugerencia de BO cuando el cruce tiene tendencia por duración.</summary>
    public static string DurationReason(DurationTrend trend) =>
        trend.FavorsShort ? "el cruce se gana más en partidas cortas" : "el cruce se gana más en partidas largas";

    /// <param name="AllEloN">Las partidas del mismo mapa con todas las franjas de Elo (para saber si el cero es del mapa).</param>
    private sealed record Resumen(string Headline, MatchupCell General, int Map, int EloBand, string Place, string? MapLabel,
        int AllEloN);

    private static Resumen? Summary(MatchupStats stats, MatchupQuery query, Func<string, string> civName, Func<string, string?> mapName)
    {
        if (stats.IsEmpty || (!query.Team && query.Rival is null))
            return null;
        var map = stats.MapIndex(query.Map);
        var eloBand = stats.EloBand(query.Elo);
        var general = query.Team ? stats.Team(query.Civ, map, eloBand, 0) : stats.OneVsOne(query.Civ, query.Rival!, map, eloBand, 0);
        if (general is null)
            return null;
        var allElo = eloBand == 0 ? general
            : query.Team ? stats.Team(query.Civ, map, 0, 0) : stats.OneVsOne(query.Civ, query.Rival!, map, 0, 0);
        var mapLabel = map > 0 ? mapName(stats.MapKeys[map - 1]) ?? stats.MapKeys[map - 1] : null;
        var place = mapLabel is null ? "en todos los mapas" : $"en {mapLabel}";
        var elo = query.Elo is > 0 ? $"Elo {query.Elo}" : "sin tu Elo";
        var chance = $"{(general.Even ? "parejo, " : "")}{(query.Team ? "" : "ganás ")}{Range(general)}";
        var headline = query.Team
            ? $"{Capitalized(civName(query.Civ))} {place} por equipos ({elo}): {chance}."
            : $"{Capitalized(civName(query.Civ))} contra {civName(query.Rival!)} {place} ({elo}): {chance}.";
        return new Resumen(headline, general, map, eloBand, place, mapLabel, allElo?.N ?? 0);
    }

    private static string? Missing(MatchupStats stats, MatchupQuery query, Func<string, string> civName)
    {
        if (stats.IsEmpty || (!query.Team && query.Rival is null))
            return null;
        if (!stats.HasCiv(query.Civ))
            return $"Sin datos de partidas clasificatorias de {civName(query.Civ)}.";
        return !query.Team && !stats.HasCiv(query.Rival!) ? $"Sin datos de partidas clasificatorias de {civName(query.Rival!)}." : null;
    }

    private static string Estimated(Resumen resumen, MatchupQuery query)
    {
        if (resumen.General.N > 0)
            return "";
        // El cero puede venir de la franja de Elo y no del mapa: en Bosque Negro con 1600 o más falta en la mitad de las parejas.
        var donde = (resumen.MapLabel is null ? "" : $" en {resumen.MapLabel}") + (resumen.AllEloN > 0 ? " con tu Elo" : "");
        return query.Team ? $" Estimado: no hay partidas de esta civ{donde}." : $" Estimado: no hay partidas de este cruce{donde}.";
    }

    private static string Duration(MatchupStats stats, MatchupQuery query, Resumen resumen)
    {
        var trend = query.Team
            ? stats.TeamTrend(query.Civ, resumen.Map, resumen.EloBand)
            : stats.OneVsOneTrend(query.Civ, query.Rival!, resumen.Map, resumen.EloBand);
        if (trend is not null)
        {
            if (query.Team)
                return trend.FavorsShort ? " Mejor en partidas cortas." : " Mejor en partidas largas.";
            return $" Mejor si dura {(trend.FavorsShort ? "poco" : "mucho")}: {Percent(trend.Short.P)} % "
                   + $"{stats.DurationLabel(trend.ShortBand)}, {Percent(trend.Long.P)} % {stats.DurationLabel(trend.LongBand)}.";
        }
        var conPartidas = Enumerable.Range(1, 5).Count(band => (query.Team
            ? stats.Team(query.Civ, resumen.Map, resumen.EloBand, band)
            : stats.OneVsOne(query.Civ, query.Rival!, resumen.Map, resumen.EloBand, band)) is { N: > 0 });
        return conPartidas >= 2 ? " Parejo en cualquier duración." : "";
    }

    private static string Range(MatchupCell cell) => $"{Percent(cell.P)} % ({Percent(cell.From)} a {Percent(cell.To)})";

    private static int Percent(double p) => (int)Math.Round(p * 100, MidpointRounding.AwayFromZero);

    // Invariante: el Core corre también en WebAssembly con la globalización invariante (sube igual "á" a "Á").
    private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
