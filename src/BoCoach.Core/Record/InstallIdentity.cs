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
    /// <summary>
    /// La identidad guardada, o una nueva si no hay o lo guardado no sirve (para la base es una instalación nueva). Si no
    /// se puede leer el archivo (un antivirus lo tiene tomado) tira: otra identidad escrita encima dejaría afuera de
    /// "Borrar lo compartido" todo lo que subió esta.
    /// </summary>
    public static InstallIdentity LoadOrCreate(string path)
    {
        if (File.Exists(path) && Parse(File.ReadAllText(path)) is { } saved)
            return saved;
        var created = new InstallIdentity(Guid.NewGuid().ToString("N"), Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        // Por un temporal: si se corta a la mitad, no queda un archivo roto en lugar del que había.
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, new JsonObject { ["id"] = created.Id, ["secret"] = created.Secret }.ToJsonString() + "\n");
        File.Move(temporary, path, overwrite: true);
        return created;
    }

    private static InstallIdentity? Parse(string text)
    {
        try
        {
            return JsonNode.Parse(text, documentOptions: CoreJson.NoDuplicates) is JsonObject json
                   && CoreJson.Text(json["id"]) is { } id && IdPattern().IsMatch(id)
                   && CoreJson.Text(json["secret"]) is { } secret && SecretPattern().IsMatch(secret)
                ? new InstallIdentity(id, secret)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex SecretPattern();
}
