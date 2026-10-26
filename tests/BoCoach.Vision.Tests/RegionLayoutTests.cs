using BoCoach.Vision.Hud;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Regions;

namespace BoCoach.Vision.Tests;

public sealed class RegionLayoutTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"regions-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void El_default_tiene_las_11_regiones()
    {
        // Las 9 que se leen más la cola de producción y los puntajes, que por ahora solo se graban.
        Assert.Equal(11, RegionNames.All.Count);
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
        Assert.Equal(1, loaded.HudScale); // calibraciones de antes de la escala del HUD
    }

    // B4 (b): la calibración sola mide el tamaño del HUD; se guarda para reescalar las capturas al leerlas.
    [Fact]
    public void La_escala_del_HUD_se_guarda_y_la_calibracion_manual_la_vuelve_a_1()
    {
        var layout = RegionLayout.Default.WithHudScale(1.09);

        layout.Save(_path);
        var loaded = RegionLayout.LoadOrDefault(_path);

        Assert.Equal(1.09, loaded.HudScale, 3);
        Assert.Contains("\"hud_scale\"", File.ReadAllText(_path));
        Assert.Equal(1.09, loaded.With(RegionNames.Food, new RegionRect(0.1, 0.2, 0.03, 0.02)).HudScale, 3);
        Assert.Equal(1, loaded.WithReference(1920, 1080).HudScale);
    }

    [Fact]
    public void Archivo_mal_formado_da_error_claro()
    {
        File.WriteAllText(_path, """{ "regions": { "food": [0.1, 0.1] } }""");

        var e = Assert.Throws<InvalidDataException>(() => RegionLayout.LoadOrDefault(_path));
        Assert.Contains("food", e.Message);
    }

    private static HudLayout Hud() => HudLayout.Load(Path.Combine(AppContext.BaseDirectory, "hud_layout.json"));

    [Fact]
    public void Guarda_y_carga_el_diseno_del_HUD()
    {
        var hud = Hud();
        RegionLayout.Default.WithHud(hud).WithHudScale(1.5).Save(_path);

        var loaded = RegionLayout.LoadOrDefault(_path);

        Assert.Equal("bocoach-2026-10", loaded.Hud!.Name);
        Assert.Equal(hud.ToJson(), loaded.Hud.ToJson());
        Assert.Equal(1.5, loaded.HudScale);
    }

    /// <summary>
    /// El diseño guardado lo escribió la app misma; si una versión nueva lo valida más estricto, el archivo del usuario
    /// se abre igual, sin diseño (y vuelve a calibrar), en vez de fallar en cada arranque del coach.
    /// </summary>
    [Fact]
    public void Un_diseno_guardado_que_no_se_entiende_se_ignora_y_las_regiones_se_cargan()
    {
        File.WriteAllText(_path, """{ "regions": { "food": [0.1, 0.2, 0.03, 0.02] }, "hud_layout": { "name": "roto" } }""");

        var loaded = RegionLayout.LoadOrDefault(_path);

        Assert.Null(loaded.Hud);
        Assert.Equal(0.1, loaded.Regions[RegionNames.Food].X, 5);
    }

    [Fact]
    public void La_calibracion_manual_conserva_el_diseno()
    {
        var layout = RegionLayout.Default.WithHud(Hud()).WithReference(2560, 1440).With(RegionNames.Food, new RegionRect(0.1, 0.2, 0.03, 0.02));

        Assert.NotNull(layout.Hud);
        Assert.Equal(2560, layout.ReferenceWidth);
    }

    [Fact]
    public void Sin_diseno_el_archivo_no_lo_trae()
    {
        RegionLayout.Default.Save(_path);

        Assert.DoesNotContain("\"name\"", File.ReadAllText(_path));
        Assert.Null(RegionLayout.LoadOrDefault(_path).Hud);
    }
}
