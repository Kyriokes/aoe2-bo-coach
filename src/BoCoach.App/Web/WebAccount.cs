using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    public WebSettings Settings { get; private set; } = Load(path ?? DefaultPath);

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
        Settings = settings;
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
                WebUrl = json["web_url"]?.GetValue<string>() is { Length: > 0 } url ? url : WebSettings.DefaultUrl,
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
