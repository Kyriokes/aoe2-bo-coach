using System.Text.Json;
using System.Text.Json.Nodes;

namespace BoCoach.Core.BuildOrders;

public sealed class BuildOrderFormatException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Lee y escribe build orders en JSON. Solo revisa el formato; las reglas las revisa <see cref="BuildOrderValidator"/>.</summary>
public static class BuildOrderSerializer
{
    public static BuildOrder Load(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (BuildOrderFormatException e)
        {
            throw new BuildOrderFormatException($"{Path.GetFileName(path)}: {e.Message}", e);
        }
    }

    public static BuildOrder Parse(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException e)
        {
            throw new BuildOrderFormatException($"JSON inválido (línea {e.LineNumber + 1}): {e.Message}", e);
        }

        if (root is not JsonObject obj)
            throw new BuildOrderFormatException("La build order tiene que ser un objeto JSON.");

        var version = ReadSchemaVersion(obj);
        if (version > BuildOrder.CurrentSchemaVersion)
        {
            throw new BuildOrderFormatException(
                $"La build order usa schema_version {version}, pero esta versión de la app solo entiende hasta la {BuildOrder.CurrentSchemaVersion}. Actualizá la app.");
        }

        Migrate(obj, version);

        try
        {
            return obj.Deserialize(CoreJson.Info<BuildOrder>())
                ?? throw new BuildOrderFormatException("La build order está vacía.");
        }
        catch (JsonException e)
        {
            throw new BuildOrderFormatException($"Valor inválido en {Describe(e.Path)}: {e.Message}", e);
        }
    }

    public static string Serialize(BuildOrder buildOrder) => JsonSerializer.Serialize(buildOrder, CoreJson.Info<BuildOrder>());

    public static void Save(BuildOrder buildOrder, string path) => File.WriteAllText(path, Serialize(buildOrder) + "\n");

    private static int ReadSchemaVersion(JsonObject obj)
    {
        if (!obj.TryGetPropertyValue("schema_version", out var node) || node is null)
            throw new BuildOrderFormatException("Falta 'schema_version'.");
        try
        {
            return node.GetValue<int>();
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException)
        {
            throw new BuildOrderFormatException("'schema_version' tiene que ser un número entero.", e);
        }
    }

    /// <summary>Lleva una BO de una versión vieja del formato a la actual. Por ahora solo existe la 1.</summary>
    private static void Migrate(JsonObject obj, int fromVersion)
    {
        if (fromVersion < 1)
            throw new BuildOrderFormatException($"schema_version {fromVersion} no existe.");
        obj["schema_version"] = BuildOrder.CurrentSchemaVersion;
    }

    private static string Describe(string? path) =>
        string.IsNullOrEmpty(path) || path == "$" ? "la raíz" : path.TrimStart('$', '.');
}
