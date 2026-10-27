using System.Text.Json;
using System.Text.RegularExpressions;

namespace BoCoach.Core.Data;

public sealed class GameDataException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Carga y valida data/game_data/&lt;versión&gt;.json.</summary>
public static partial class GameDataLoader
{
    public static GameData Load(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (GameDataException e)
        {
            throw new GameDataException($"{Path.GetFileName(path)}: {e.Message}", e);
        }
    }

    /// <summary>Carga el archivo de versión más alta (v8.9.json, v8.10.json, ...) de una carpeta.</summary>
    public static GameData LoadLatest(string directory)
    {
        var latest = Directory.EnumerateFiles(directory, "v*.json")
            .Select(path => (path, version: ParseVersion(Path.GetFileNameWithoutExtension(path))))
            .Where(f => f.version is not null)
            .MaxBy(f => f.version);

        return latest.path is null
            ? throw new GameDataException($"No hay archivos v<versión>.json en {directory}.")
            : Load(latest.path);
    }

    public static GameData Parse(string json)
    {
        GameDataDto dto;
        try
        {
            dto = JsonSerializer.Deserialize(json, CoreJson.Info<GameDataDto>())
                ?? throw new GameDataException("El archivo está vacío.");
        }
        catch (JsonException e)
        {
            throw new GameDataException($"JSON inválido en {e.Path ?? "la raíz"}: {e.Message}", e);
        }

        if (string.IsNullOrWhiteSpace(dto.DatVersion))
            throw new GameDataException("Falta 'dat_version'.");
        if (dto.Entities is null || dto.Entities.Count == 0)
            throw new GameDataException("Falta 'entities' o está vacío.");

        var entities = dto.Entities.ToDictionary(e => e.Key, e => ToEntity(e.Key, e.Value));
        foreach (var entity in entities.Values)
        {
            if (entity.MadeIn is not null && !entities.ContainsKey(entity.MadeIn))
                throw new GameDataException($"'{entity.Key}': made_in '{entity.MadeIn}' no es una entidad conocida.");
            if (entity.Requires is { } required && (!entities.TryGetValue(required, out var requiredEntity) || requiredEntity.Kind != EntityKind.Building))
                throw new GameDataException($"'{entity.Key}': requires '{required}' no es un edificio conocido.");
            foreach (var counted in entity.CountsAs)
            {
                if (!entities.TryGetValue(counted, out var other) || other.Kind != EntityKind.Building)
                    throw new GameDataException($"'{entity.Key}': counts_as '{counted}' no es un edificio conocido.");
            }
        }

        var civs = (dto.Civs ?? []).ToDictionary(c => c.Key, c => ToCiv(c.Key, c.Value, entities));
        return new GameData(dto.DatVersion, entities, ToGatherRates(dto.GatherRates), civs, ToCarry(dto.GatherCarry), ToFood(dto.FoodPerObject));
    }

    private static Dictionary<GatherSource, double>? ToFood(Dictionary<string, double>? food)
    {
        if (food is null)
            return null;
        var result = new Dictionary<GatherSource, double>();
        foreach (var (name, amount) in food)
        {
            if (!Enum.TryParse<GatherSource>(name, ignoreCase: true, out var source))
                throw new GameDataException($"food_per_object: fuente desconocida '{name}'.");
            result[source] = amount;
        }
        return result;
    }

    private static Entity ToEntity(string key, EntityDto dto)
    {
        string Missing(string field) => $"'{key}': falta '{field}'.";

        var kind = dto.Kind ?? throw new GameDataException(Missing("kind"));
        if (kind != EntityKind.Building && dto.MadeIn is null)
            throw new GameDataException(Missing("made_in"));
        if (dto.Time is null or < 0)
            throw new GameDataException($"'{key}': 'time' falta o es negativo.");

        var cost = ResourceAmounts.Zero;
        foreach (var (name, amount) in dto.Cost ?? [])
        {
            if (!Enum.TryParse<Resource>(name, ignoreCase: true, out var resource))
                throw new GameDataException($"'{key}': recurso desconocido '{name}' en cost.");
            cost = cost.With(resource, amount);
        }

        var gather = new Dictionary<GatherSource, GatherEffect>();
        foreach (var (name, effect) in dto.Gather ?? [])
        {
            if (!Enum.TryParse<GatherSource>(name, ignoreCase: true, out var source))
                throw new GameDataException($"'{key}': fuente desconocida '{name}' en gather.");
            if (effect.Carry is { Length: not 2 })
                throw new GameDataException($"'{key}': gather.{name}.carry tiene que ser [multiplicador, suma].");
            gather[source] = new GatherEffect(effect.Work ?? 1, effect.Carry?[0] ?? 1, effect.Carry?[1] ?? 0, effect.Speed ?? 1);
        }

        return new Entity(
            key,
            kind,
            dto.DatId ?? throw new GameDataException(Missing("dat_id")),
            dto.DatName ?? "",
            cost,
            dto.Time.Value,
            dto.Age ?? throw new GameDataException(Missing("age")),
            dto.MadeIn,
            dto.Pop,
            dto.PopBonus)
        {
            Gather = gather,
            CountsAs = dto.CountsAs ?? [],
            FarmFood = dto.FarmFood ?? 0,
            Requires = dto.Requires,
        };
    }

    private static CivBonuses ToCiv(string key, CivDto dto, IReadOnlyDictionary<string, Entity> entities)
    {
        T[] Ages<T>(T[] values, string field) => values.Length == 4
            ? values
            : throw new GameDataException($"civs.{key}.{field}: tiene que tener un valor por edad (4) y tiene {values.Length}.");

        string Known(string entity, string field) => entities.ContainsKey(entity)
            ? entity
            : throw new GameDataException($"civs.{key}.{field}: '{entity}' no es una entidad conocida.");

        var gather = new Dictionary<GatherSource, double[]>();
        foreach (var (name, values) in dto.Gather ?? [])
        {
            if (!Enum.TryParse<GatherSource>(name, ignoreCase: true, out var source))
                throw new GameDataException($"civs.{key}.gather: fuente desconocida '{name}'.");
            gather[source] = Ages(values, $"gather.{name}");
        }

        ResourceAmounts ToAmounts(Dictionary<string, double> cost, string field)
        {
            var amounts = ResourceAmounts.Zero;
            foreach (var (name, amount) in cost)
            {
                if (!Enum.TryParse<Resource>(name, ignoreCase: true, out var resource))
                    throw new GameDataException($"civs.{key}.{field}: recurso desconocido '{name}'.");
                amounts = amounts.With(resource, amount);
            }
            return amounts;
        }

        var start = dto.Start is { } s
            ? new CivStart(new ResourceAmounts(s.Food, s.Wood, s.Stone, s.Gold), s.Villagers, s.NoHouses)
            : CivStart.None;

        var carry = new Dictionary<GatherSource, double[]>();
        foreach (var (name, values) in dto.Carry ?? [])
        {
            if (!Enum.TryParse<GatherSource>(name, ignoreCase: true, out var source))
                throw new GameDataException($"civs.{key}.carry: fuente desconocida '{name}'.");
            carry[source] = Ages(values, $"carry.{name}");
        }

        var foodYield = new Dictionary<GatherSource, double[]>();
        foreach (var (name, values) in dto.FoodYield ?? [])
        {
            if (!Enum.TryParse<GatherSource>(name, ignoreCase: true, out var source))
                throw new GameDataException($"civs.{key}.food_yield: fuente desconocida '{name}'.");
            foodYield[source] = Ages(values, $"food_yield.{name}");
        }

        var ageResources = new Dictionary<Age, ResourceAmounts>();
        foreach (var (name, amounts) in dto.AgeResources ?? [])
        {
            if (!Enum.TryParse<Age>(name, ignoreCase: true, out var age) || age == Age.Dark)
                throw new GameDataException($"civs.{key}.age_resources: edad desconocida '{name}'.");
            ageResources[age] = ToAmounts(amounts, $"age_resources.{name}");
        }
        return new CivBonuses(
            key,
            dto.DatName ?? "",
            gather,
            dto.BuildRate is { } build ? Ages(build, "build_rate") : null,
            (dto.Costs ?? []).ToDictionary(c => Known(c.Key, "costs"), c => Ages(c.Value.ToArray(), $"costs.{c.Key}").Select(a => ToAmounts(a, $"costs.{c.Key}")).ToArray()),
            (dto.Times ?? []).ToDictionary(t => Known(t.Key, "times"), t => Ages(t.Value, $"times.{t.Key}")),
            (dto.WorkRate ?? []).ToDictionary(w => Known(w.Key, "work_rate"), w => Ages(w.Value, $"work_rate.{w.Key}")),
            (dto.PopBonus ?? []).ToDictionary(p => Known(p.Key, "pop_bonus"), p => Ages(p.Value, $"pop_bonus.{p.Key}")),
            start,
            carry,
            dto.VillagerSpeed is { } speed ? Ages(speed, "villager_speed") : null,
            ageResources)
        {
            Names = dto.Names ?? new Dictionary<string, string>(),
            Unavailable = (dto.Unavailable ?? []).ToHashSet(),
            NoRequires = (dto.NoRequires ?? []).Select(k => Known(k, "no_requires")).ToHashSet(),
            FoodYields = foodYield,
            FarmUpgradeFoods = dto.FarmUpgradeFood is { } upgrades ? Ages(upgrades, "farm_upgrade_food") : null,
            FarmExtras = dto.FarmExtra is { } extra ? Ages(extra, "farm_extra") : null,
        };
    }

    private static Dictionary<GatherSource, double>? ToCarry(Dictionary<string, double>? carry)
    {
        if (carry is null)
            return null;
        var result = new Dictionary<GatherSource, double>();
        foreach (var (name, amount) in carry)
        {
            if (!Enum.TryParse<GatherSource>(name, ignoreCase: true, out var source))
                throw new GameDataException($"Fuente desconocida '{name}' en gather_carry.");
            if (amount <= 0)
                throw new GameDataException($"gather_carry.{name} tiene que ser mayor a 0.");
            result[source] = amount;
        }
        return result;
    }

    private static Dictionary<GatherSource, double> ToGatherRates(Dictionary<string, double>? rates)
    {
        if (rates is null)
            throw new GameDataException("Falta 'gather_rates'.");

        var result = new Dictionary<GatherSource, double>();
        foreach (var (name, rate) in rates)
        {
            if (!Enum.TryParse<GatherSource>(name, ignoreCase: true, out var source))
                throw new GameDataException($"Fuente desconocida '{name}' en gather_rates.");
            if (rate <= 0)
                throw new GameDataException($"gather_rates.{name} tiene que ser mayor a 0.");
            result[source] = rate;
        }

        var missing = Enum.GetValues<GatherSource>().Where(s => !result.ContainsKey(s)).ToList();
        if (missing.Count > 0)
            throw new GameDataException($"Faltan tasas en gather_rates: {string.Join(", ", missing)}.");
        return result;
    }

    private static Version? ParseVersion(string fileName)
    {
        var match = VersionPattern().Match(fileName);
        return match.Success && Version.TryParse(match.Groups[1].Value + (match.Groups[1].Value.Contains('.') ? "" : ".0"), out var v)
            ? v
            : null;
    }

    [GeneratedRegex(@"^v(\d+(?:\.\d+){0,3})$")]
    private static partial Regex VersionPattern();

    internal sealed record GameDataDto(
        string? DatVersion,
        Dictionary<string, EntityDto>? Entities,
        Dictionary<string, double>? GatherRates,
        Dictionary<string, CivDto>? Civs,
        Dictionary<string, double>? GatherCarry,
        Dictionary<string, double>? FoodPerObject);

    internal sealed record CivDto(
        string? DatName,
        Dictionary<string, double[]>? Gather,
        double[]? BuildRate,
        Dictionary<string, List<Dictionary<string, double>>>? Costs,
        Dictionary<string, double[]>? Times,
        Dictionary<string, double[]>? WorkRate,
        Dictionary<string, int[]>? PopBonus,
        CivStartDto? Start,
        Dictionary<string, double[]>? Carry,
        double[]? VillagerSpeed,
        Dictionary<string, Dictionary<string, double>>? AgeResources,
        Dictionary<string, string>? Names,
        List<string>? Unavailable,
        List<string>? NoRequires,
        Dictionary<string, double[]>? FoodYield,
        double[]? FarmUpgradeFood,
        double[]? FarmExtra);

    internal sealed record CivStartDto(int Food, int Wood, int Stone, int Gold, int Villagers, bool NoHouses);

    internal sealed record EntityDto(
        EntityKind? Kind,
        int? DatId,
        string? DatName,
        Dictionary<string, double>? Cost,
        int? Time,
        Age? Age,
        string? MadeIn,
        int Pop,
        int PopBonus,
        Dictionary<string, GatherEffectDto>? Gather,
        string[]? CountsAs,
        double? FarmFood,
        string? Requires);

    internal sealed record GatherEffectDto(double? Work, double[]? Carry, double? Speed);
}
