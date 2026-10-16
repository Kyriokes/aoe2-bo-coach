using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests.Data;

/// <summary>data/events.json, generado por el extractor desde los textos y los árboles de tecnologías del juego.</summary>
public class EventCatalogTests
{
    private static readonly EventCatalog Events = TestData.Events;

    [Fact]
    public void Toda_entidad_de_game_data_tiene_su_evento()
    {
        foreach (var (key, entity) in TestData.GameData.Entities)
        {
            var action = entity.Kind switch
            {
                EntityKind.Unit => StepAction.Train,
                EntityKind.Building => StepAction.Build,
                _ => StepAction.Research,
            };
            var eventId = EventConventions.EventFor(action, key)!;
            Assert.True(Events.Contains(eventId), $"falta {eventId}");
        }
    }

    [Theory]
    [InlineData("villager_created", "Villager Created")]         // el nombre interno es "Villager (Male)"
    [InlineData("trade_cart_created", "Trade Cart Created")]     // "Trade Cart (Empty)"
    [InlineData("eagle_scout_created", "Eagle Scout Created")]
    [InlineData("militia_created", "Levy Created")]              // otro nombre en las Crónicas, mismo evento
    [InlineData("palisade_wall_built", "Palisade Wall Built")]
    [InlineData("fortified_wall_researched", "Fortified Wall Research Complete")]
    [InlineData("elite_skirmisher_researched", "Elite Skirmisher Research Complete")]
    [InlineData("farm_exhausted", "Farm Exhausted")]
    [InlineData("under_attack", "Warning: You are being attacked by")]
    public void Frases_del_juego(string eventId, string phrase)
    {
        Assert.Contains(phrase, Events.Phrases[eventId]);
    }

    [Fact]
    public void Ninguna_frase_es_de_dos_eventos()
    {
        var repeated = Events.Phrases
            .SelectMany(e => e.Value.Select(p => (Event: e.Key, Phrase: p.ToLowerInvariant())))
            .GroupBy(p => p.Phrase)
            .Where(g => g.Select(p => p.Event).Distinct().Count() > 1)
            .Select(g => g.Key);
        Assert.Empty(repeated);
    }
}
