using BoCoach.Vision.Digits;
using BoCoach.Vision.Hud;

namespace BoCoach.Vision.Tests;

/// <summary>
/// Las letras que vienen en data/fonts. La del diseño nuevo (hud_numbers, 48 pt) se armó primero con las plantillas del
/// mod viejo: la misma letra del juego al mismo tamaño, más el "/" de mod_population. Sin ella, con el mod nuevo el coach
/// no lee ningún número.
/// </summary>
public class HudFontsTests
{
    private static readonly string FontsDir = Path.Combine(AppContext.BaseDirectory, "fonts");

    [Fact]
    public void La_letra_del_diseno_nuevo_viene_con_la_app_con_digitos_y_barra()
    {
        var fonts = HudFonts.Load(FontsDir);

        Assert.NotNull(fonts.HudNumbers);
        Assert.Equal("hud_numbers", fonts.HudNumbers.Name);
        var chars = fonts.HudNumbers.Templates.Select(t => t.Char).ToHashSet();
        Assert.Superset("0123456789/".ToHashSet(), chars);
        Assert.Equal(GlyphFont.Load(Path.Combine(FontsDir, "mod_numbers.json")).Ink, fonts.HudNumbers.Ink);
    }
}
