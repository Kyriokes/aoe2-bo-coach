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
        Assert.Empty(Repeated(Events));
    }

    private static IEnumerable<string> Repeated(EventCatalog catalog) => catalog.Phrases
        .SelectMany(e => e.Value.Select(p => (Event: e.Key, Phrase: p.ToLowerInvariant())))
        .GroupBy(p => p.Phrase)
        .Where(g => g.Select(p => p.Event).Distinct().Count() > 1)
        .Select(g => g.Key);

    // ---- Juego en castellano (amigos del usuario, 2026-10-20): events.es.json (España) y events.mx.json (Latinoamérica) ----

    private static EventCatalog Spanish(string language) => EventCatalog.Load(Path.Combine(AppContext.BaseDirectory, $"events.{language}.json"));

    [Theory]
    [InlineData("es", "villager_created", "Aldeano: creación finalizada")]
    [InlineData("es", "house_built", "Casa: construcción finalizada")]
    [InlineData("es", "feudal_age_researched", "Edad Feudal: desarrollo finalizado")]
    [InlineData("es", "farm_exhausted", "Granja agotada")]                 // el juego la escribe "-Granja agotada--"
    [InlineData("es", "spearman_created", "Lancero: creación finalizada")] // y no la variante del caballero de una civ
    [InlineData("es", "knight_created", "Caballero: creación finalizada")]
    [InlineData("mx", "villager_created", "Aldeano: creación finalizada")]
    [InlineData("mx", "knight_created", "Jinete: creación finalizada")]
    [InlineData("mx", "spearman_created", "Lancero: creación finalizada")]
    public void Frases_del_juego_en_castellano(string language, string eventId, string phrase)
    {
        Assert.Contains(phrase, Spanish(language).Phrases[eventId]);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("mx")]
    public void En_castellano_toda_entidad_de_game_data_tiene_su_evento_y_ninguna_frase_es_de_dos(string language)
    {
        var catalog = Events.Merged(Spanish(language));

        foreach (var (key, entity) in TestData.GameData.Entities)
        {
            var action = entity.Kind switch
            {
                EntityKind.Unit => StepAction.Train,
                EntityKind.Building => StepAction.Build,
                _ => StepAction.Research,
            };
            var eventId = EventConventions.EventFor(action, key)!;
            Assert.True(Spanish(language).Contains(eventId), $"falta {eventId}");
        }
        Assert.Empty(Repeated(catalog));
    }

    [Fact]
    public void Unido_al_ingles_tiene_las_frases_de_los_dos_y_la_primera_de_cada_idioma_es_la_de_siempre()
    {
        var catalog = Events.Merged(Spanish("es")).PreferringEntitiesOf(TestData.GameData);

        var villager = catalog.Phrases["villager_created"];
        Assert.Equal("Villager Created", villager[0]);
        var aldeano = villager.ToList().IndexOf("Aldeano: creación finalizada");
        Assert.True(aldeano > 0);
        Assert.True(catalog.IsCommon("villager_created", 0));
        Assert.True(catalog.IsCommon("villager_created", aldeano));
        // Las variantes de otras civs siguen siendo raras, en los dos idiomas.
        var knight = catalog.Phrases["knight_created"].ToList();
        Assert.False(catalog.IsCommon("knight_created", knight.IndexOf("Lancer Created")));
    }
}
