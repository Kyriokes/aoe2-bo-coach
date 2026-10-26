using System.Text.Encodings.Web;
using System.Text.Json;

namespace BoCoach.Core.Web;

/// <summary>Un jugador con ese nombre en la API del juego: su perfil, país y Elo de mapa aleatorio.</summary>
public sealed record EloCandidate(string Alias, long ProfileId, string? Country, int? Elo1v1, int? EloTeam, DateTimeOffset? LastMatch);

/// <summary>El resultado de buscar un nombre: los candidatos o un error para mostrarle al usuario.</summary>
public sealed record EloSearch(IReadOnlyList<EloCandidate> Candidates, string? Error);

/// <summary>
/// D10: el Elo de un jugador por su nombre, de la API oficial del juego (la que usan aoe2companion y aoestats; no
/// está documentada). Solo se consulta cuando el usuario toca "Buscar". La respuesta trae hasta 10 jugadores con ese
/// nombre exacto (statGroups) y el Elo de cada uno por tabla (leaderboardStats: 3 = 1v1 y 4 = equipos, de mapa
/// aleatorio). Verificado el 2026-10-25.
/// </summary>
public static class EloLookup
{
    public const string BaseUrl = "https://aoe-api.worldsedgelink.com";
    public const string UserAgent = "aoe2-bo-coach (+https://github.com/Kyriokes/aoe2-bo-coach)";
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private const long OneVsOneBoard = 3;
    private const long TeamBoard = 4;
    private const long UnknownAliases = 9;
    private const long MaxUnixSeconds = 253_402_300_799; // 9999-12-31

    /// <summary>La consulta de un nombre: el parámetro aliases es un arreglo JSON con el nombre.</summary>
    public static Uri Address(string alias)
    {
        var aliases = $"[\"{JsonEncodedText.Encode(alias.Trim(), JavaScriptEncoder.UnsafeRelaxedJsonEscaping)}\"]";
        return new Uri($"{BaseUrl}/community/leaderboard/GetPersonalStat?title=age2&aliases={Uri.EscapeDataString(aliases)}");
    }

    private const string NotUnderstood = "La API del juego respondió algo que no entiendo: probá más tarde.";

    /// <summary>Los candidatos de una respuesta, el que jugó más recientemente primero; vacío si no hay o no se entiende.</summary>
    public static IReadOnlyList<EloCandidate> Parse(string json) => Read(json).Candidates;

    /// <summary>
    /// Una respuesta: los candidatos, o un error si la API avisó otra cosa que "ese nombre no existe" (código 9) o si
    /// no se entiende. Así un límite de pedidos no se lee como "no encontré a ese jugador".
    /// </summary>
    public static EloSearch Read(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new([], NotUnderstood);
            if (root.TryGetProperty("result", out var result) && Number(result, "code", out var code) && code != 0)
                return code == UnknownAliases ? new([], null) : new([], $"La API del juego respondió un error (código {code}): probá más tarde.");
            if (!root.TryGetProperty("statGroups", out var groups) || groups.ValueKind != JsonValueKind.Array)
                return new([], NotUnderstood);
            var boards = Boards(root);
            var candidates = new List<EloCandidate>();
            foreach (var group in groups.EnumerateArray())
            {
                if (!Number(group, "id", out var id) || !group.TryGetProperty("members", out var members)
                    || members.ValueKind != JsonValueKind.Array || members.GetArrayLength() == 0)
                    continue;
                var member = members[0];
                if (Text(member, "alias") is not { } alias || !Number(member, "profile_id", out var profile))
                    continue;
                var (oneVsOne, team, last) = boards.GetValueOrDefault(id);
                candidates.Add(new EloCandidate(alias, profile, Text(member, "country"), oneVsOne, team, Date(last)));
            }
            return new(candidates.OrderByDescending(c => c.LastMatch ?? DateTimeOffset.MinValue).ToList(), null);
        }
        catch (JsonException)
        {
            return new([], NotUnderstood);
        }
    }

    /// <summary>La fecha de la última partida: segundos unix (o milisegundos, por si la API cambia); null si no tiene sentido.</summary>
    private static DateTimeOffset? Date(long seconds)
    {
        if (seconds > 100_000_000_000)
            seconds /= 1000;
        return seconds is > 0 and <= MaxUnixSeconds ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
    }

    /// <summary>Busca el nombre en la API (10 s como mucho). Un nombre que no existe no es error: no hay candidatos.</summary>
    public static async Task<EloSearch> SearchAsync(HttpClient http, string alias, CancellationToken cancel = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(Timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Address(alias));
            request.Headers.UserAgent.ParseAdd(UserAgent);
            using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new([], $"La API del juego respondió {(int)response.StatusCode}: probá más tarde.");
            return Read(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            return new([], $"No se pudo consultar la API del juego: no respondió en {Timeout.TotalSeconds:0} s.");
        }
        catch (HttpRequestException)
        {
            // Sin el mensaje de .NET: viene en inglés ("No such host is known").
            return new([], "No se pudo consultar la API del juego: revisá la conexión.");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Una respuesta demasiado grande o con un charset raro: nunca un cartel de error (spec D10, 6).
            return new([], "No se pudo consultar la API del juego.");
        }
    }

    /// <summary>Por grupo de estadísticas: el Elo de 1v1, el de equipos y la última partida (segundos unix).</summary>
    private static Dictionary<long, (int? OneVsOne, int? Team, long Last)> Boards(JsonElement root)
    {
        var boards = new Dictionary<long, (int? OneVsOne, int? Team, long Last)>();
        if (!root.TryGetProperty("leaderboardStats", out var stats) || stats.ValueKind != JsonValueKind.Array)
            return boards;
        foreach (var stat in stats.EnumerateArray())
        {
            if (!Number(stat, "statgroup_id", out var group) || !Number(stat, "leaderboard_id", out var board)
                || !Number(stat, "rating", out var rating) || rating is <= 0 or > int.MaxValue)
                continue;
            var (oneVsOne, team, last) = boards.GetValueOrDefault(group);
            if (board == OneVsOneBoard)
                oneVsOne = (int)rating;
            else if (board == TeamBoard)
                team = (int)rating;
            else
                continue;
            boards[group] = (oneVsOne, team, Math.Max(last, Number(stat, "lastmatchdate", out var date) ? date : 0));
        }
        return boards;
    }

    private static bool Number(JsonElement element, string name, out long value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var property)
               && property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out value);
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
}
