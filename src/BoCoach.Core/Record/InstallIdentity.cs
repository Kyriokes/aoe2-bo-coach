using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BoCoach.Core.Record;

/// <summary>
/// Quién manda los registros: un identificador aleatorio por instalación, no atado a la cuenta de la web, y un secreto
/// que la base guarda como hash la primera vez (así nadie más puede mandar ni borrar en su nombre).
/// </summary>
public sealed partial record InstallIdentity(string Id, string Secret)
{
    public static InstallIdentity LoadOrCreate(string path)
    {
        try
        {
            if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject json
                && json["id"] is JsonValue id && id.TryGetValue<string>(out var idText) && IdPattern().IsMatch(idText)
                && json["secret"] is JsonValue secret && secret.TryGetValue<string>(out var secretText) && SecretPattern().IsMatch(secretText))
                return new InstallIdentity(idText, secretText);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // Un archivo roto: se arma otra. Para la base es una instalación nueva.
        }
        var created = new InstallIdentity(Guid.NewGuid().ToString("N"), Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, new JsonObject { ["id"] = created.Id, ["secret"] = created.Secret }.ToJsonString() + "\n");
        return created;
    }

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex SecretPattern();
}
