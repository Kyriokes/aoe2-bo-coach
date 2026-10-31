using System.Text.Json;
using System.Text.Json.Nodes;
using BoCoach.Core.BuildOrders;

namespace BoCoach.Core.Web;

/// <summary>
/// Las BO de la app viven en la web: acá se guardan la última lista ("mis BO", <c>mis-bo.json</c>) y cada BO usada
/// (<c>bo/&lt;código&gt;.json</c>, con la fecha de la web). Una BO se baja recién cuando se va a usar y de nuevo solo si
/// la lista dice que cambió; sin internet se sigue con lo guardado. Lo guardado que no se puede leer se ignora.
/// </summary>
/// <param name="client">El cliente de la web; se pide en cada pedido (la dirección de la web se puede cambiar).</param>
/// <param name="token">El token de la app (null o vacío sin cuenta): se lee en cada pedido.</param>
public sealed class BuildOrderCache(string directory, Func<WebLibraryClient> client, Func<string?> token) : IBuildOrderSource
{
    public BuildOrderCache(string directory, WebLibraryClient client, Func<string?> token) : this(directory, () => client, token)
    {
    }

    private const string ListFile = "mis-bo.json";
    private const string BuildOrdersFolder = "bo";

    public string Directory { get; } = directory;

    /// <summary>La última lista: la guardada al empezar y la de la web después de cada <see cref="RefreshAsync"/> que salió.</summary>
    public WebBuildOrderList List { get; private set; } = ReadList(Path.Combine(directory, ListFile));

    /// <summary>Pide la lista a la web. Si sale la guarda; si no, sigue la anterior. Devuelve el error, o null.</summary>
    public async Task<string?> RefreshAsync(CancellationToken cancel = default)
    {
        if (Token() is not { } current)
            return "Conectá tu cuenta para elegir BO.";
        try
        {
            List = await client().MyBuildOrdersAsync(current, cancel);
        }
        catch (WebLibraryException e) when (e.Status == System.Net.HttpStatusCode.Unauthorized)
        {
            return $"Conectá tu cuenta de nuevo: {e.Message}";
        }
        catch (WebLibraryException e)
        {
            return e.Message;
        }
        Write(Path.Combine(Directory, ListFile), new JsonObject
        {
            ["account"] = List.Account,
            ["items"] = new JsonArray(List.Items.Select(ToJson).ToArray<JsonNode?>()),
        });
        return null;
    }

    public bool IsSaved(string code) => Saved(code) is not null;

    /// <summary>La BO guardada de ese código (con el código como id), o null.</summary>
    public BuildOrder? Saved(string code) => ReadSaved(code)?.BuildOrder;

    /// <summary>Todas las guardadas que se pueden leer (para elegir sin internet).</summary>
    public IEnumerable<BuildOrder> AllSaved()
    {
        var folder = Path.Combine(Directory, BuildOrdersFolder);
        if (!System.IO.Directory.Exists(folder))
            yield break;
        foreach (var file in System.IO.Directory.EnumerateFiles(folder, "*.json"))
        {
            if (ReadSaved(Path.GetFileNameWithoutExtension(file)) is { } saved)
                yield return saved.BuildOrder;
        }
    }

    /// <summary>
    /// La BO para usar: la guardada si está al día con la lista; si no, la baja (y la guarda). Sin internet, la guardada
    /// aunque sea vieja; si no hay ninguna, <see cref="WebLibraryException"/>.
    /// </summary>
    public async Task<BuildOrder> GetAsync(string code, CancellationToken cancel = default)
    {
        var saved = ReadSaved(code);
        var entry = List.Items.FirstOrDefault(i => i.Code == code);
        if (saved is not null && (entry is null || saved.UpdatedAt >= entry.UpdatedAt))
            return saved.BuildOrder;
        try
        {
            var downloaded = await client().DownloadAsync(code, Token(), cancel);
            var updated = downloaded.UpdatedAt ?? entry?.UpdatedAt ?? DateTimeOffset.MinValue;
            Write(PathFor(code), new JsonObject
            {
                ["updated_at"] = updated.ToString("O"),
                ["build_order"] = JsonNode.Parse(BuildOrderSerializer.Serialize(downloaded.BuildOrder)),
            });
            return downloaded.BuildOrder with { Id = code };
        }
        catch (WebLibraryException) when (saved is not null)
        {
            return saved.BuildOrder;
        }
    }

    /// <summary>Suma una BO por su código: se suscribe (si no es propia), pide la lista y la baja. Devuelve su entrada.</summary>
    public async Task<WebBuildOrderEntry?> AddAsync(string code, CancellationToken cancel = default)
    {
        if (Token() is not { } current)
            throw new WebLibraryException("Conectá tu cuenta para sumar BO.");
        await client().SubscribeAsync(code, current, cancel);
        if (await RefreshAsync(cancel) is { } error)
            throw new WebLibraryException(error);
        await GetAsync(code, cancel);
        return List.Items.FirstOrDefault(i => i.Code == code);
    }

    private string? Token() => token() is { Length: > 0 } value ? value : null;

    private string PathFor(string code) => Path.Combine(Directory, BuildOrdersFolder, $"{code}.json");

    private sealed record SavedBuildOrder(BuildOrder BuildOrder, DateTimeOffset UpdatedAt);

    private SavedBuildOrder? ReadSaved(string code)
    {
        if (!WebCode.IsValid(code) || ReadJson(PathFor(code)) is not { } json)
            return null;
        try
        {
            var bo = BuildOrderSerializer.Parse(json["build_order"]?.ToJsonString() ?? "");
            var updated = json["updated_at"] is JsonValue value && value.TryGetValue<string>(out var text)
                && DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var date)
                ? date
                : DateTimeOffset.MinValue;
            return new SavedBuildOrder(bo with { Id = code }, updated);
        }
        catch (BuildOrderFormatException)
        {
            return null;
        }
    }

    private static WebBuildOrderList ReadList(string path) =>
        ReadJson(path) is { } json ? WebLibraryClient.ParseList(json) : WebBuildOrderList.Empty;

    private static JsonObject? ReadJson(string path)
    {
        try
        {
            return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : null;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Escribe por un archivo temporal: si se corta a la mitad, queda el anterior. Si no se puede, sigue sin guardar.</summary>
    private static void Write(string path, JsonObject json)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, json.ToJsonString());
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static JsonObject ToJson(WebBuildOrderEntry e) => new()
    {
        ["code"] = e.Code,
        ["name"] = e.Name,
        ["civ"] = e.Civ,
        ["map"] = e.Map,
        ["style"] = e.Style,
        ["tags"] = new JsonArray(e.Tags.Select(t => (JsonNode?)t).ToArray()),
        ["author"] = e.Author,
        ["guide_author"] = e.GuideAuthor,
        ["own"] = e.Own,
        ["visibility"] = e.Visibility,
        ["updated_at"] = e.UpdatedAt.ToString("O"),
        ["feudal_click"] = e.FeudalClick,
        ["castle_click"] = e.CastleClick,
    };
}
