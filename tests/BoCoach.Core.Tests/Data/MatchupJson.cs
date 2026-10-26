using System.Text;

namespace BoCoach.Core.Tests.Data;

/// <summary>Arma un matchup_stats.json de prueba con el formato que escribe tools/stats.</summary>
internal static class MatchupJson
{
    public static int[] Cell(int p, int from, int to, int n) => [p, from, to, n];

    /// <summary>Cada función da [p, desde, hasta, n] para (mapa 0..4, Elo 0..5, duración 0..5).</summary>
    public static string Build(IReadOnlyDictionary<string, Func<int, int, int, int[]>> oneVsOne,
        IReadOnlyDictionary<string, Func<int, int, int, int[]>> team, bool previousMixed = false)
    {
        var texto = new StringBuilder();
        texto.Append("""
            {
              "schema_version": 1,
              "generated": "2026-10-25",
              "source": { "name": "aoe2-guide", "url": "https://aoe2-guide.vercel.app/data", "from": "2026-06-01", "to": "2026-07-03" },
            """);
        texto.Append($$"""  "patch": { "label": "Update 185872", "start": "2026-06-01", "previous_mixed": {{(previousMixed ? "true" : "false")}} },""");
        texto.Append("""
              "matches": { "1v1": 100, "team": 50 },
              "bands": { "elo": [850, 1050, 1300, 1600], "game_minutes": [20, 30, 40, 55], "maps": ["arabia", "arena", "black_forest", "nomad"] },
              "interval": 0.9,
            """);
        texto.Append("\"one_vs_one\": {").Append(Entries(oneVsOne)).Append("},\n");
        texto.Append("\"team\": {").Append(Entries(team)).Append("}\n}\n");
        return texto.ToString();
    }

    private static string Entries(IReadOnlyDictionary<string, Func<int, int, int, int[]>> entradas) =>
        string.Join(",\n", entradas.Select(e => $"\"{e.Key}\": [{string.Join(",", Values(e.Value))}]"));

    private static IEnumerable<int> Values(Func<int, int, int, int[]> celda)
    {
        for (var mapa = 0; mapa < 5; mapa++)
            for (var elo = 0; elo < 6; elo++)
                for (var duracion = 0; duracion < 6; duracion++)
                    foreach (var valor in celda(mapa, elo, duracion))
                        yield return valor;
    }

    /// <summary>Todos los casilleros iguales.</summary>
    public static Func<int, int, int, int[]> Same(int p, int from, int to, int n) => (_, _, _) => Cell(p, from, to, n);
}
