using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;

namespace BoCoach.App.Editor;

/// <summary>Opción de un ComboBox: valor y texto a mostrar.</summary>
public sealed record Option<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

public static class Options
{
    public static IReadOnlyList<Option<StepAction>> Actions { get; } =
        Enum.GetValues<StepAction>().Select(a => new Option<StepAction>(a, StepText.Action(a))).ToList();

    /// <summary>Fuentes de recolección, con una opción vacía al principio.</summary>
    public static IReadOnlyList<Option<GatherSource?>> Sources { get; } =
        new[] { new Option<GatherSource?>(null, "(sin asignar)") }
            .Concat(Enum.GetValues<GatherSource>().Select(s => new Option<GatherSource?>(s, StepText.Source(s))))
            .ToList();

    public static string AgeShort(Age age) => age switch
    {
        Age.Dark => "Oscura",
        Age.Feudal => "Feudal",
        Age.Castle => "Castillos",
        _ => "Imperial",
    };

    /// <summary>Entidades que se pueden usar con cada acción, ordenadas por edad y nombre.</summary>
    public static IReadOnlyList<Option<string?>> EntitiesFor(GameData data, StepAction action)
    {
        var kind = action switch
        {
            StepAction.Train => EntityKind.Unit,
            StepAction.Build => EntityKind.Building,
            StepAction.Research => EntityKind.Tech,
            _ => (EntityKind?)null,
        };
        if (kind is null)
            return [];
        return data.Entities.Values
            .Where(e => e.Kind == kind)
            .OrderBy(e => e.Age)
            .ThenBy(e => StepText.Name(e.Key), StringComparer.CurrentCultureIgnoreCase)
            .Select(e => new Option<string?>(e.Key, $"{Capitalize(StepText.Name(e.Key))} ({AgeShort(e.Age)})"))
            .ToList();
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
