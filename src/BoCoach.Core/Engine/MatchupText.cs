using BoCoach.Core.Data;
using static BoCoach.Core.Lang;

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
        return stats.PreviousMixed ? texto + L(" (Con datos del parche anterior.)", " (With data from the previous patch.)") : texto;
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
        var duro = L($"Duró {(int)gameLength.TotalMinutes} min: a esa duración, ", $"It lasted {(int)gameLength.TotalMinutes} min: at that length, ");
        var segundo = query.Team
            ? L($"{duro}los equipos con {civName(query.Civ)} ganan el {Range(cell!)}.", $"{duro}teams with {civName(query.Civ)} win {Range(cell!)}.")
            : L($"{duro}en este cruce ganás el {Range(cell!)}.", $"{duro}you win {Range(cell!)} of this matchup.");
        if (cell!.N == 0)
            segundo += L(" Estimado: pocas partidas así.", " Estimated: few games like this.");
        return [resumen.Headline + Estimated(resumen, query), segundo];
    }

    /// <summary>El porqué de la sugerencia de BO cuando el cruce tiene tendencia por duración.</summary>
    public static string DurationReason(DurationTrend trend) =>
        trend.FavorsShort ? L("el cruce se gana más en partidas cortas", "this matchup is won more in short games") : L("el cruce se gana más en partidas largas", "this matchup is won more in long games");

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
        var place = mapLabel is null ? L("en todos los mapas", "on all maps") : L($"en {mapLabel}", $"on {mapLabel}");
        var elo = query.Elo is > 0 ? $"Elo {query.Elo}" : L("sin tu Elo", "without your Elo");
        var chance = $"{(general.Even ? L("parejo, ", "even, ") : "")}{(query.Team ? "" : L("ganás ", "you win "))}{Range(general)}";
        var headline = query.Team
            ? L($"{Capitalized(civName(query.Civ))} {place} por equipos ({elo}): {chance}.", $"{Capitalized(civName(query.Civ))} {place} in team games ({elo}): {chance}.")
            : L($"{Capitalized(civName(query.Civ))} contra {civName(query.Rival!)} {place} ({elo}): {chance}.", $"{Capitalized(civName(query.Civ))} vs {civName(query.Rival!)} {place} ({elo}): {chance}.");
        return new Resumen(headline, general, map, eloBand, place, mapLabel, allElo?.N ?? 0);
    }

    private static string? Missing(MatchupStats stats, MatchupQuery query, Func<string, string> civName)
    {
        if (stats.IsEmpty || (!query.Team && query.Rival is null))
            return null;
        if (!stats.HasCiv(query.Civ))
            return L($"Sin datos de partidas clasificatorias de {civName(query.Civ)}.", $"No ranked game data for {civName(query.Civ)}.");
        return !query.Team && !stats.HasCiv(query.Rival!) ? L($"Sin datos de partidas clasificatorias de {civName(query.Rival!)}.", $"No ranked game data for {civName(query.Rival!)}.") : null;
    }

    private static string Estimated(Resumen resumen, MatchupQuery query)
    {
        if (resumen.General.N > 0)
            return "";
        // El cero puede venir de la franja de Elo y no del mapa: en Bosque Negro con 1600 o más falta en la mitad de las parejas.
        var donde = (resumen.MapLabel is null ? "" : L($" en {resumen.MapLabel}", $" on {resumen.MapLabel}")) + (resumen.AllEloN > 0 ? L(" con tu Elo", " at your Elo") : "");
        return query.Team
            ? L($" Estimado: no hay partidas de esta civ{donde}.", $" Estimated: no games of this civ{donde}.")
            : L($" Estimado: no hay partidas de este cruce{donde}.", $" Estimated: no games of this matchup{donde}.");
    }

    private static string Duration(MatchupStats stats, MatchupQuery query, Resumen resumen)
    {
        var trend = query.Team
            ? stats.TeamTrend(query.Civ, resumen.Map, resumen.EloBand)
            : stats.OneVsOneTrend(query.Civ, query.Rival!, resumen.Map, resumen.EloBand);
        if (trend is not null)
        {
            if (query.Team)
                return trend.FavorsShort ? L(" Mejor en partidas cortas.", " Better in short games.") : L(" Mejor en partidas largas.", " Better in long games.");
            return L($" Mejor si dura {(trend.FavorsShort ? "poco" : "mucho")}: ", $" Better if it's {(trend.FavorsShort ? "short" : "long")}: ")
                   + $"{Percent(trend.Short.P)} % {stats.DurationLabel(trend.ShortBand)}, {Percent(trend.Long.P)} % {stats.DurationLabel(trend.LongBand)}.";
        }
        var conPartidas = Enumerable.Range(1, 5).Count(band => (query.Team
            ? stats.Team(query.Civ, resumen.Map, resumen.EloBand, band)
            : stats.OneVsOne(query.Civ, query.Rival!, resumen.Map, resumen.EloBand, band)) is { N: > 0 });
        // "Sin diferencia clara", no "parejo": que los rangos se toquen no prueba que no haya diferencia (spec 4.5), y
        // "parejo" después de "ganás 45 %" se leía como una contradicción.
        return conPartidas >= 2 ? L(" Sin diferencia clara por duración.", " No clear difference by game length.") : "";
    }

    private static string Range(MatchupCell cell) => L($"{Percent(cell.P)} % ({Percent(cell.From)} a {Percent(cell.To)})", $"{Percent(cell.P)} % ({Percent(cell.From)} to {Percent(cell.To)})");

    private static int Percent(double p) => (int)Math.Round(p * 100, MidpointRounding.AwayFromZero);

    // Invariante: el Core corre también en WebAssembly con la globalización invariante (sube igual "á" a "Á").
    private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
