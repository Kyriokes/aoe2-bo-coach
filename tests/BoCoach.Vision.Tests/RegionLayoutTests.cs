using BoCoach.Vision.Capture;
using BoCoach.Vision.Regions;

namespace BoCoach.Vision.Tests;

public sealed class RegionLayoutTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"regions-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void El_default_tiene_las_9_regiones()
    {
        Assert.Equal(9, RegionNames.All.Count);
        Assert.All(RegionNames.All, name => Assert.True(RegionLayout.Default.Regions.ContainsKey(name)));
        Assert.All(RegionNames.All, name => Assert.NotEqual(name, RegionNames.Label(name)));
    }

    [Fact]
    public void Fracciones_a_pixeles_en_distintas_resoluciones()
    {
        var rect = new RegionRect(0.5, 0.25, 0.1, 0.05);

        Assert.Equal(new PixelRect(960, 270, 192, 54), rect.ToPixels(1920, 1080));
        Assert.Equal(new PixelRect(1280, 360, 256, 72), rect.ToPixels(2560, 1440));
    }

    [Fact]
    public void Relativo_a_la_ventana_del_juego_en_el_segundo_monitor()
    {
        var area = new PixelRect(1920, 0, 1920, 1080);

        var food = new RegionRect(0.5, 0.25, 0.1, 0.05).ToPixels(area);

        Assert.Equal(new PixelRect(1920 + 960, 270, 192, 54), food);
    }

    [Fact]
    public void Los_pixeles_nunca_se_salen_de_la_pantalla()
    {
        var px = new RegionRect(0.99, 0.99, 0.2, 0.2).ToPixels(100, 100);

        Assert.True(px.X + px.Width <= 100 && px.Y + px.Height <= 100);
        Assert.False(px.IsEmpty);
    }

    [Fact]
    public void Guardar_y_cargar()
    {
        var layout = RegionLayout.Default
            .With(RegionNames.Food, new RegionRect(0.1, 0.2, 0.03, 0.02))
            .WithReference(2560, 1440);

        layout.Save(_path);
        var loaded = RegionLayout.LoadOrDefault(_path);

        Assert.Equal(2560, loaded.ReferenceWidth);
        Assert.Equal(new RegionRect(0.1, 0.2, 0.03, 0.02), loaded.Regions[RegionNames.Food]);
        Assert.Contains("\"reference_resolution\"", File.ReadAllText(_path));
        Assert.Contains("\"event_log\"", File.ReadAllText(_path));
    }

    [Fact]
    public void Si_no_hay_archivo_usa_el_default_y_completa_regiones_faltantes()
    {
        Assert.Same(RegionLayout.Default, RegionLayout.LoadOrDefault(_path));

        File.WriteAllText(_path, """{ "reference_resolution": [1920, 1080], "regions": { "food": [0.1, 0.1, 0.05, 0.02] } }""");
        var loaded = RegionLayout.LoadOrDefault(_path);

        Assert.Equal(RegionNames.All.Count, loaded.Regions.Count);
        Assert.Equal(0.1, loaded.Regions[RegionNames.Food].X);
    }

    [Fact]
    public void Archivo_mal_formado_da_error_claro()
    {
        File.WriteAllText(_path, """{ "regions": { "food": [0.1, 0.1] } }""");

        var e = Assert.Throws<InvalidDataException>(() => RegionLayout.LoadOrDefault(_path));
        Assert.Contains("food", e.Message);
    }
}
