using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Engine;
using BoCoach.Core.Web;

namespace BoCoach.App.Web;

/// <summary>Datos de la cuenta de la biblioteca web (los edita el diálogo "Cuenta de la web").</summary>
public sealed record WebSettings
{
    /// <summary>Dirección de la web por defecto (el proyecto en Vercel).</summary>
    public const string DefaultUrl = "https://aoe2-bo-coach.vercel.app";

    public string WebUrl { get; init; } = DefaultUrl;

    /// <summary>Token personal de la app (se crea en "Mi cuenta" de la web). Vacío = sin cuenta.</summary>
    public string Token { get; init; } = "";

    /// <summary>Código en la web de cada BO local publicada desde la app (id local → código), para reemplazarla.</summary>
    public IReadOnlyDictionary<string, string> Published { get; init; } = new Dictionary<string, string>();

    public bool HasToken => Token.Length > 0;

    public Uri BaseUri => new(WebUrl.TrimEnd('/') + "/");
}

/// <summary>
/// La cuenta de la web en esta PC: %APPDATA%\BoCoach\web.json. El token se guarda cifrado con DPAPI (solo lo puede
/// descifrar el mismo usuario de Windows), nunca en texto plano.
/// </summary>
public sealed class WebAccount(HttpClient http, string? path = null)
{
    public static string DefaultPath => Path.Combine(CoachData.UserDir, "web.json");

    private static readonly byte[] Entropy = "BoCoach.WebToken"u8.ToArray();

    public string FilePath { get; } = path ?? DefaultPath;

    // La versión del archivo se toma antes de leerlo: si cambia en el medio, la próxima vez se relee.
    private (DateTime, long) _loadedWrite = Version(path ?? DefaultPath);
    private WebSettings _settings = Load(path ?? DefaultPath);

    /// <summary>Hora y tamaño: dos escrituras seguidas pueden caer en la misma hora del disco.</summary>
    private static (DateTime, long) Version(string path)
    {
        var file = new FileInfo(path);
        return file.Exists ? (file.LastWriteTimeUtc, file.Length) : default;
    }

    /// <summary>
    /// Lo guardado. Si otra instancia cambió el archivo (BoCoach.exe --publish con la app abierta), se relee: si no,
    /// la app pisaría los códigos nuevos al guardar y volvería a publicar como nuevas las BO ya publicadas.
    /// </summary>
    public WebSettings Settings
    {
        get
        {
            var write = Version(FilePath);
            if (write != _loadedWrite)
                (_loadedWrite, _settings) = (write, Load(FilePath));
            return _settings;
        }
    }

    public WebLibraryClient Client => new(http, Settings.BaseUri);

    public void Save(WebSettings settings)
    {
        var json = new JsonObject
        {
            ["web_url"] = settings.WebUrl,
            ["token"] = settings.HasToken ? Protect(settings.Token) : null,
            ["published"] = new JsonObject(settings.Published.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)p.Value))),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        (_loadedWrite, _settings) = (Version(FilePath), settings);
    }

    /// <summary>
    /// Publica la BO en la web, o la reemplaza si ya se publicó desde esta PC (mantiene su código y sus votos). Si la
    /// de la web se borró o es de otra cuenta, la publica como nueva. Los clics de edad los estima el Planner.
    /// </summary>
    public async Task<PublishedBuildOrder> PublishAsync(BuildOrder bo, Planner planner, CancellationToken cancel = default)
    {
        var plan = planner.Simulate(bo);
        var (feudal, castle) = (plan.ClickTime("feudal_age"), plan.ClickTime("castle_age"));
        var code = Settings.Published.GetValueOrDefault(bo.Id);
        PublishedBuildOrder published;
        try
        {
            published = await Client.PublishAsync(bo, Settings.Token, feudal, castle, code, cancel);
        }
        catch (WebLibraryException e) when (code is not null && e.Status is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            ForgetPublished(bo.Id);
            published = await Client.PublishAsync(bo, Settings.Token, feudal, castle, cancel: cancel);
        }
        RememberPublished(bo.Id, published.Code);
        return published;
    }

    /// <summary>Recuerda el código con que se publicó una BO local.</summary>
    public void RememberPublished(string localId, string code) =>
        Save(Settings with { Published = new Dictionary<string, string>(Settings.Published) { [localId] = code } });

    /// <summary>Se olvida del código (por ejemplo, la BO se borró de la web).</summary>
    public void ForgetPublished(string localId)
    {
        var published = new Dictionary<string, string>(Settings.Published);
        if (published.Remove(localId))
            Save(Settings with { Published = published });
    }

    private static WebSettings Load(string path)
    {
        if (!File.Exists(path))
            return new WebSettings();
        try
        {
            var json = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? [];
            return new WebSettings
            {
                // Una dirección que no sirve (web.json editado a mano) haría fallar BaseUri al publicar o bajar.
                WebUrl = json["web_url"]?.GetValue<string>() is { Length: > 0 } url && Uri.TryCreate(url, UriKind.Absolute, out _)
                    ? url
                    : WebSettings.DefaultUrl,
                Token = json["token"]?.GetValue<string>() is { Length: > 0 } token ? Unprotect(token) : "",
                Published = (json["published"] as JsonObject ?? [])
                    .Where(p => p.Value is not null)
                    .ToDictionary(p => p.Key, p => p.Value!.GetValue<string>()),
            };
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or CryptographicException)
        {
            // Archivo roto o token de otro usuario de Windows: se empieza de cero (hay que volver a pegar el token).
            return new WebSettings();
        }
    }

    private static string Protect(string token) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(token), Entropy, DataProtectionScope.CurrentUser));

    private static string Unprotect(string data) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(data), Entropy, DataProtectionScope.CurrentUser));
}
