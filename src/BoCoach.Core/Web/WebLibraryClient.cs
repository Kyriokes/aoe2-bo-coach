using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BoCoach.Core.BuildOrders;

namespace BoCoach.Core.Web;

/// <summary>Un error de la web explicado para el usuario.</summary>
public sealed class WebLibraryException(string message, HttpStatusCode? status = null, Exception? inner = null)
    : Exception(message, inner)
{
    public HttpStatusCode? Status { get; } = status;
}

/// <summary>Una BO publicada: su código corto y la dirección de su página.</summary>
public sealed record PublishedBuildOrder(string Code, string Url);

/// <summary>Una BO bajada de la web.</summary>
/// <param name="Author">La cuenta que la subió (lo que la app muestra al confirmar).</param>
/// <param name="GuideAuthor">El autor que dice el JSON (lo escribe cualquiera).</param>
/// <param name="UpdatedAt">Cuándo se cambió por última vez en la web (la caché la baja de nuevo si cambió).</param>
public sealed record WebBuildOrder(string Code, string Author, BuildOrder BuildOrder, DateTimeOffset? UpdatedAt = null, string GuideAuthor = "");

/// <summary>Una BO de la lista de la app ("mis BO"): tuya o suscripta, sin los pasos.</summary>
public sealed record WebBuildOrderEntry(string Code, string Name, string Civ, string? Map, string? Style, IReadOnlyList<string> Tags,
    string Author, string GuideAuthor, bool Own, string Visibility, DateTimeOffset UpdatedAt, double? FeudalClick, double? CastleClick)
{
    /// <summary>Lo que usa la sugerencia para puntuarla sin bajarla.</summary>
    public BuildOrderSummary Summary => new(Code, Name, Civ, Map, Tags, Style);
}

/// <summary>La lista de la app: el nombre de la cuenta del token y sus BO.</summary>
public sealed record WebBuildOrderList(string Account, IReadOnlyList<WebBuildOrderEntry> Items)
{
    public static WebBuildOrderList Empty { get; } = new("", []);
}

/// <summary>Los códigos de la biblioteca web: 6 caracteres sin los que se confunden (0/o, 1/l/i).</summary>
public static partial class WebCode
{
    [GeneratedRegex("^[a-z2-9]{6}$")]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"(?:^|bocoach://import/|/bo/|/api/bo/)([a-z2-9]{6})(?:[/?#].*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex AnyForm();

    public static bool IsValid(string code) => CodePattern().IsMatch(code);

    /// <summary>
    /// El código a partir de lo que pegue el usuario: el código solo, un enlace bocoach://import/&lt;código&gt; o la
    /// dirección de la página de la BO.
    /// </summary>
    public static string? Parse(string? text)
    {
        var match = AnyForm().Match((text ?? "").Trim());
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }
}

/// <summary>
/// La API de la biblioteca web (ver web/README.md): bajar una BO por su código y publicar o reemplazar una con el
/// token personal que se crea en "Mi cuenta" de la web.
/// </summary>
public sealed class WebLibraryClient(HttpClient http, Uri baseUrl)
{
    public Uri BaseUrl { get; } = baseUrl;

    /// <summary>
    /// El token solo viaja por https, o a esta misma PC (la web en desarrollo): por http cualquiera en la red (un wifi
    /// público) lo puede leer y publicar en nombre del usuario.
    /// </summary>
    public static bool SendsTokenSafely(Uri url) => url.Scheme == Uri.UriSchemeHttps || (url.Scheme == Uri.UriSchemeHttp && url.IsLoopback);

    /// <summary>Baja una BO por su código. Con el token (de la app) también las privadas propias.</summary>
    public async Task<WebBuildOrder> DownloadAsync(string code, string? token = null, CancellationToken cancel = default)
    {
        CheckCode(code);
        if (token is not null)
            CheckTokenSafe();
        using var response = await Send(WithToken(new HttpRequestMessage(HttpMethod.Get, new Uri(BaseUrl, $"api/bo/{code}")), token), cancel);
        var json = await ReadJson(response, cancel);
        try
        {
            var bo = BuildOrderSerializer.Parse(json["build_order"]?.ToJsonString() ?? "");
            var author = Text(json["author"]) ?? "Anónimo";
            return new WebBuildOrder(code, author, bo, Date(json["updated_at"]), Text(json["guide_author"]) ?? author);
        }
        catch (BuildOrderFormatException e)
        {
            throw new WebLibraryException($"La BO de la web no se pudo leer: {e.Message}", inner: e);
        }
    }

    /// <summary>La lista de la app: tus BO (también privadas) y tus suscriptas. Una entrada que no se entiende se saltea.</summary>
    public async Task<WebBuildOrderList> MyBuildOrdersAsync(string token, CancellationToken cancel = default)
    {
        CheckTokenSafe();
        using var response = await Send(WithToken(new HttpRequestMessage(HttpMethod.Get, new Uri(BaseUrl, "api/app/mis-bo")), token), cancel);
        return ParseList(await ReadJson(response, cancel));
    }

    /// <summary>Lee la lista como la manda la web (también la guardada por la caché).</summary>
    public static WebBuildOrderList ParseList(JsonObject json)
    {
        var items = new List<WebBuildOrderEntry>();
        foreach (var node in json["items"] as JsonArray ?? [])
        {
            if (node is not JsonObject item || Text(item["code"]) is not { } code || !WebCode.IsValid(code)
                || Text(item["name"]) is not { Length: > 0 } name || Date(item["updated_at"]) is not { } updated)
                continue;
            var author = Text(item["author"]) ?? "Anónimo";
            items.Add(new WebBuildOrderEntry(code, name, Text(item["civ"]) ?? "any", Text(item["map"]), Text(item["style"]),
                (item["tags"] as JsonArray ?? []).Select(Text).OfType<string>().ToList(), author, Text(item["guide_author"]) ?? author,
                item["own"] is JsonValue own && own.TryGetValue<bool>(out var isOwn) && isOwn,
                Text(item["visibility"]) ?? "public", updated, Number(item["feudal_click"]), Number(item["castle_click"])));
        }
        return new WebBuildOrderList(Text(json["account"]) ?? "", items);
    }

    /// <summary>Suma una BO pública a la lista de la app. True si es propia (las propias ya están).</summary>
    public async Task<bool> SubscribeAsync(string code, string token, CancellationToken cancel = default)
    {
        CheckCode(code);
        CheckTokenSafe();
        using var response = await Send(WithToken(new HttpRequestMessage(HttpMethod.Post, new Uri(BaseUrl, $"api/app/suscripciones/{code}")), token), cancel);
        var json = await ReadJson(response, cancel);
        return json["own"] is JsonValue own && own.TryGetValue<bool>(out var isOwn) && isOwn;
    }

    private static void CheckCode(string code)
    {
        if (!WebCode.IsValid(code))
            throw new WebLibraryException($"\"{code}\" no es un código de la biblioteca (son 6 letras y números).");
    }

    private void CheckTokenSafe()
    {
        if (!SendsTokenSafely(BaseUrl))
            throw new WebLibraryException(
                $"La dirección de la web ({BaseUrl}) no es https: el token viajaría sin cifrar. Cambiala en \"Cuenta de la web…\".");
    }

    private static HttpRequestMessage WithToken(HttpRequestMessage request, string? token)
    {
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static double? Number(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<double>(out var number) ? number : null;

    private static DateTimeOffset? Date(JsonNode? node) =>
        Text(node) is { } text && DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var date) ? date : null;

    /// <summary>Publica la BO (o la reemplaza si se pasa el código de una propia).</summary>
    /// <param name="feudalClick">Clic de Feudal estimado por el Planner, en segundos (se muestra en la biblioteca).</param>
    public async Task<PublishedBuildOrder> PublishAsync(BuildOrder bo, string token, double? feudalClick, double? castleClick,
        string? code = null, CancellationToken cancel = default)
    {
        CheckTokenSafe();
        if (code is not null)
            CheckCode(code);
        var body = new JsonObject
        {
            ["build_order"] = JsonNode.Parse(BuildOrderSerializer.Serialize(bo)),
            ["feudal_click"] = feudalClick is { } f ? Math.Round(f) : null,
            ["castle_click"] = castleClick is { } c ? Math.Round(c) : null,
        };
        var request = new HttpRequestMessage(code is null ? HttpMethod.Post : HttpMethod.Put, new Uri(BaseUrl, code is null ? "api/bo" : $"api/bo/{code}"))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await Send(request, cancel);
        var json = await ReadJson(response, cancel);
        return new PublishedBuildOrder(json["code"]!.GetValue<string>(), json["url"]!.GetValue<string>());
    }

    private async Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken cancel)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancel);
        }
        catch (HttpRequestException e)
        {
            throw new WebLibraryException($"No se pudo conectar con {BaseUrl.Host}: {e.Message}", inner: e);
        }
        catch (TaskCanceledException e) when (!cancel.IsCancellationRequested)
        {
            throw new WebLibraryException($"{BaseUrl.Host} tardó demasiado en responder.", inner: e);
        }
        finally
        {
            request.Dispose();
        }

        if (response.IsSuccessStatusCode)
            return response;
        var message = await ErrorMessage(response, cancel);
        response.Dispose();
        throw new WebLibraryException(message, response.StatusCode);
    }

    private static async Task<JsonObject> ReadJson(HttpResponseMessage response, CancellationToken cancel)
    {
        try
        {
            return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancel)) as JsonObject
                ?? throw new WebLibraryException("La web respondió algo que no es un objeto JSON.");
        }
        catch (JsonException e)
        {
            throw new WebLibraryException("La web respondió algo que no es JSON (¿la dirección es la de la biblioteca?).", inner: e);
        }
        catch (HttpRequestException e)
        {
            // Más grande que MaxResponseContentBufferSize del HttpClient.
            throw new WebLibraryException("La web respondió algo demasiado grande para ser una BO.", inner: e);
        }
    }

    /// <summary>El mensaje que manda la web ({ "error": "..." }) o uno según el código HTTP.</summary>
    private static async Task<string> ErrorMessage(HttpResponseMessage response, CancellationToken cancel)
    {
        try
        {
            if (JsonNode.Parse(await response.Content.ReadAsStringAsync(cancel)) is JsonObject { } obj
                && obj["error"]?.GetValue<string>() is { Length: > 0 } error)
                return error;
        }
        catch (JsonException)
        {
        }
        return response.StatusCode switch
        {
            HttpStatusCode.NotFound => "No está en la biblioteca.",
            HttpStatusCode.Unauthorized => "El token de la app no es válido: creá uno nuevo en Mi cuenta de la web.",
            HttpStatusCode.Forbidden => "Esa BO no es tuya.",
            _ => $"La web respondió {(int)response.StatusCode} ({response.ReasonPhrase}).",
        };
    }
}
