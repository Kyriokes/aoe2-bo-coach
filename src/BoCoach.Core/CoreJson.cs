using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;

namespace BoCoach.Core;

/// <summary>
/// Opciones de JSON compartidas: snake_case en propiedades y enums, como en los archivos del repo.
/// Los serializadores se generan al compilar (<see cref="CoreJsonContext"/>), sin reflexión: así el mismo Core
/// corre recortado en WebAssembly para la web.
/// </summary>
public static class CoreJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters =
        {
            Enum<StepAction>(),
            Enum<GatherSource>(),
            Enum<EntityKind>(),
            Enum<Age>(),
            Enum<Resource>(),
        },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        TypeInfoResolver = CoreJsonContext.Default,
    };

    /// <summary>Metadatos de un tipo con estas opciones (las sobrecargas que los usan son seguras al recortar).</summary>
    public static JsonTypeInfo<T> Info<T>() => (JsonTypeInfo<T>)Options.GetTypeInfo(typeof(T));

    private static JsonStringEnumConverter<T> Enum<T>() where T : struct, System.Enum =>
        new(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false);
}

/// <summary>Los tipos que el Core lee y escribe en JSON.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DictionaryKeyPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(BuildOrder))]
[JsonSerializable(typeof(Dictionary<string, List<string>>))]
[JsonSerializable(typeof(GameDataLoader.GameDataDto))]
internal sealed partial class CoreJsonContext : JsonSerializerContext;
