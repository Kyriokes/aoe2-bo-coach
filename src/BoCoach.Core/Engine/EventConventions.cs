using BoCoach.Core.BuildOrders;

namespace BoCoach.Core.Engine;

/// <summary>
/// Convención de ids de evento: "&lt;entidad&gt;_created" para unidades, "&lt;entidad&gt;_built" para edificios
/// y "&lt;entidad&gt;_researched" para tecnologías. Un paso sin 'confirm_by' se confirma con su evento por convención.
/// </summary>
public static class EventConventions
{
    public const string VillagerCreated = "villager_created";

    /// <summary>
    /// Eventos que el juego no avisa en el log aunque estén en events.json: las granjas terminadas (en las partidas
    /// 20261018-001030, 011721 y 021924 hubo granjas y ningún "Farm Built"). Un paso así no espera al log.
    /// </summary>
    public static readonly IReadOnlySet<string> Unannounced = new HashSet<string> { "farm_built" };

    public static string? EventFor(StepAction action, string? what) => what is null
        ? null
        : action switch
        {
            StepAction.Train => $"{what}_created",
            StepAction.Build => $"{what}_built",
            StepAction.Research => $"{what}_researched",
            _ => null,
        };

    /// <summary>Evento que confirma cada unidad de trabajo del paso, o null si no hay (move).</summary>
    public static string? EventFor(Step step) => step.ConfirmBy ?? EventFor(step.Do, step.What);

    /// <summary>Acción y entidad que representa un evento, si sigue la convención.</summary>
    public static (StepAction Action, string What)? Parse(string eventId)
    {
        foreach (var (suffix, action) in Suffixes)
        {
            if (eventId.EndsWith(suffix, StringComparison.Ordinal) && eventId.Length > suffix.Length)
                return (action, eventId[..^suffix.Length]);
        }
        return null;
    }

    public static bool IsUnitCreated(string eventId) => eventId.EndsWith("_created", StringComparison.Ordinal);

    private static readonly (string Suffix, StepAction Action)[] Suffixes =
    [
        ("_created", StepAction.Train),
        ("_built", StepAction.Build),
        ("_researched", StepAction.Research),
    ];
}
