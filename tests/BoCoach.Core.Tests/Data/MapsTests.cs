using BoCoach.Core.Data;

namespace BoCoach.Core.Tests.Data;

/// <summary>D8: los mapas de data/maps.json (qué fuentes tiene cada uno y si hay agua).</summary>
public class MapsTests
{
    private static readonly Maps Real = Maps.Load(Path.Combine(AppContext.BaseDirectory, "maps.json"));

    [Fact]
    public void El_archivo_del_repo_tiene_cualquiera_primero_y_Arabia_sin_agua()
    {
        Assert.Equal(Maps.Any, Real.All[0].Key);
        Assert.True(Real.All[0].Water);
        Assert.Equal(Enum.GetValues<GatherSource>().Length, Real.All[0].Sources.Count);

        var arabia = Real.Get("arabia");
        Assert.NotNull(arabia);
        Assert.Equal("Arabia", arabia.Name);
        Assert.False(arabia.Water);
        Assert.False(arabia.Has(GatherSource.Fish));
        Assert.True(arabia.Has(GatherSource.Hunt));

        Assert.True(Real.Get("islands")!.Water);
        Assert.Null(Real.Get("atlantis"));
        Assert.All(Real.All, m => Assert.False(string.IsNullOrWhiteSpace(m.Name)));
    }

    [Fact]
    public void Null_o_any_es_cualquier_mapa()
    {
        Assert.True(Maps.IsAny(null));
        Assert.True(Maps.IsAny(" ANY "));
        Assert.False(Maps.IsAny("arabia"));
        Assert.True(Maps.Empty.IsKnown(null));
        Assert.Equal("Cualquiera", Maps.Empty.Get(null)!.Name);
        Assert.False(Maps.Empty.IsKnown("arabia"));
    }

    [Fact]
    public void Ignora_los_valores_de_otro_tipo_y_un_archivo_vacio()
    {
        var maps = Maps.Parse("""
            { "maps": {
                "raro": { "name": 7, "water": "sí", "sources": ["wood", 3, "dragones"], "note": null },
                "": { "name": "sin clave" },
                "lista": [1, 2]
            } }
            """);

        var raro = Assert.Single(maps.All, m => m.Key != Maps.Any);
        Assert.Equal("raro", raro.Name);
        Assert.False(raro.Water);
        Assert.Equal([GatherSource.Wood], raro.Sources);
        Assert.Null(raro.Note);
        Assert.Equal([Maps.Any], Maps.Parse("").All.Select(m => m.Key));
        Assert.Equal([Maps.Any], Maps.Parse("{}").All.Select(m => m.Key));
    }

    [Fact]
    public void Un_error_de_sintaxis_dice_el_archivo()
    {
        var path = Path.Combine(Path.GetTempPath(), $"maps-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ nada");
        try
        {
            var error = Assert.Throws<InvalidDataException>(() => Maps.Load(path));
            Assert.Contains(Path.GetFileName(path), error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
