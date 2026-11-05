using System.Text.Json;
using System.Text.Json.Nodes;

namespace BoCoach.Core.BuildOrders;

public sealed class BuildOrderFormatException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Lee y escribe build orders en JSON. Solo revisa el formato; las reglas las revisa <see cref="BuildOrderValidator"/>.</summary>
public static class BuildOrderSerializer
{
    /// <summary>Cómo se lee el JSON de una BO o de una guía: se puede escribir a mano, pero una clave repetida es un error.</summary>
    internal static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        AllowDuplicateProperties = false,
    };

    /// <summary>El error de un JSON que no se pudo leer. Una clave repetida no trae el número de línea.</summary>
    internal static BuildOrderFormatException Invalid(Exception e) =>
        new(e is JsonException { LineNumber: { } line } ? $"JSON inválido (línea {line + 1}): {e.Message}" : $"JSON inválido: {e.Message}", e);

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

    /// <param name="data">
    /// Para pasar el formato 1 a totales con los aldeanos de más de la civ (ver <see cref="Engine.LivePlan.Units"/>): el
    /// editor de la web manda unidades en formato 1. Sin datos, se pasa sin ellos.
    /// </param>
    public static BuildOrder Parse(string json, Data.GameData? data = null)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: DocumentOptions);
        }
        catch (JsonException e)
        {
            throw Invalid(e);
        }
        catch (ArgumentException e)
        {
            throw Invalid(e); // una mitad de un carácter suelta en el texto: JavaScript la puede mandar
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

        BuildOrder bo;
        try
        {
            bo = obj.Deserialize(CoreJson.Info<BuildOrder>())
                ?? throw new BuildOrderFormatException("La build order está vacía.");
        }
        catch (JsonException e)
        {
            throw new BuildOrderFormatException($"Valor inválido en {Describe(e.Path)}: {e.Message}", e);
        }
        catch (InvalidOperationException e)
        {
            throw Invalid(e); // un texto que no se puede leer ("\ud800")
        }
        Normalize(bo);

        // Formato 1 → 2: en los pasos de aldeanos, 'count' pasó de "cuántos se entrenan" a "cuántos tiene que haber en
        // la fuente" (como el contador del HUD).
        if (version < 2)
            Engine.LivePlan.CountsToTotals(bo, data?.Civ(bo.Civ).Start.ExtraVillagers ?? 0);
        return bo;
    }

    /// <summary>
    /// Un null en el JSON pasa la web y la base, y los serializadores generados lo dejan tal cual aunque la propiedad no lo
    /// admita, así que después tira cualquiera que la toque. Donde hay un valor por defecto, vale lo mismo que si la clave
    /// no viniera; adentro de una lista, o en la señal de una rama, no se sabe qué quiso decir.
    /// </summary>
    private static void Normalize(BuildOrder bo)
    {
        bo.Id ??= "";
        bo.Name ??= "";
        bo.Civ ??= "any";
        bo.Tags = NoNulls(bo.Tags, "tags");
        bo.Steps = NoNulls(bo.Steps, "steps");
        Normalize(bo.Steps, "steps");
        if (bo.Posture is { } posture)
            posture.Queue = NoNulls(posture.Queue, "posture.queue");
        if (bo.Branches is not null)
        {
            bo.Branches = NoNulls(bo.Branches, "branches");
            for (var i = 0; i < bo.Branches.Count; i++)
            {
                var branch = bo.Branches[i];
                if (branch.On is null)
                    throw NullAt($"branches[{i}].on");
                branch.Steps = NoNulls(branch.Steps, $"branches[{i}].steps");
                Normalize(branch.Steps, $"branches[{i}].steps");
            }
        }
        if (bo.Assisted is { } assisted)
        {
            assisted.Opening ??= new AssistedOpening();
            assisted.Opening.Berries ??= new AssistedOpening().Berries;
            assisted.Villagers ??= new AssistedVillagers();
            assisted.Goals = NoNulls(assisted.Goals, "assisted.goals");
        }
    }

    private static void Normalize(IReadOnlyList<Step> steps, string path)
    {
        for (var i = 0; i < steps.Count; i++)
        {
            steps[i].Id ??= "";
            steps[i].After = NoNulls(steps[i].After, $"{path}[{i}].after");
        }
    }

    private static IReadOnlyList<T> NoNulls<T>(IReadOnlyList<T>? list, string path) where T : class
    {
        if (list is null)
            return [];
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] is null)
                throw NullAt($"{path}[{i}]");
        }
        return list;
    }

    private static BuildOrderFormatException NullAt(string path) => new($"Valor inválido en {path}: no puede ser null.");

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

    /// <summary>Lleva una BO de una versión vieja del formato a la actual (lo que no depende de simular).</summary>
    private static void Migrate(JsonObject obj, int fromVersion)
    {
        if (fromVersion < 1)
            throw new BuildOrderFormatException($"schema_version {fromVersion} no existe.");
        obj["schema_version"] = BuildOrder.CurrentSchemaVersion;
    }

    private static string Describe(string? path) =>
        string.IsNullOrEmpty(path) || path == "$" ? "la raíz" : path.TrimStart('$', '.');
}
