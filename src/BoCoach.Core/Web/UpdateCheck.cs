using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BoCoach.Core.Web;

/// <summary>
/// El aviso de versión nueva: al abrir, la app le pregunta a GitHub cuál es la última release y, si es más nueva que la
/// que corre, la ventana lo dice con un enlace directo al .zip. El pedido no lleva nada del usuario (ni cuenta ni token)
/// y la respuesta se toma como entrada no confiable: solo se lee la etiqueta, y solo si es vN.N.N. El enlace de
/// descarga es fijo, no sale de la respuesta: release.yml sube el mismo .zip también con el nombre sin versión.
/// </summary>
public static partial class UpdateCheck
{
    public const string LatestReleaseApi = "https://api.github.com/repos/Kyriokes/aoe2-bo-coach/releases/latest";

    /// <summary>El .zip de la última release, sin pasar por la página de GitHub (que muestra tres descargas).</summary>
    public const string DownloadUrl = "https://github.com/Kyriokes/aoe2-bo-coach/releases/latest/download/BoCoach-win-x64.zip";

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [GeneratedRegex("^v([0-9]{1,6})\\.([0-9]{1,6})\\.([0-9]{1,6})$", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();

    /// <summary>La versión de la etiqueta de la release, o null si la respuesta no es lo que se espera.</summary>
    public static Version? ParseLatest(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("tag_name", out var tag)
                || tag.ValueKind != JsonValueKind.String)
                return null;
            var match = TagPattern().Match(tag.GetString()!);
            if (!match.Success)
                return null;
            return new Version(
                int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Si la de GitHub es más nueva; de la versión de la app cuentan solo las tres primeras partes.</summary>
    public static bool IsNewer(Version latest, Version current) =>
        latest > new Version(current.Major, current.Minor, Math.Max(current.Build, 0));

    /// <summary>La versión nueva, o null si no hay, no hay internet o GitHub responde otra cosa (no falla nunca por eso).</summary>
    public static async Task<Version?> NewerVersionAsync(HttpClient http, Version current, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
            request.Headers.UserAgent.ParseAdd(EloLookup.UserAgent);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;
            var latest = ParseLatest(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            return latest is not null && IsNewer(latest, current) ? latest : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return null;
        }
    }
}
