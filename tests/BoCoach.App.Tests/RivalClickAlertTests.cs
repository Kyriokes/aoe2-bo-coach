using BoCoach.App.Overlay;
using BoCoach.Core.Data;

namespace BoCoach.App.Tests;

/// <summary>
/// Pedido del usuario del 2026-10-31: el aviso del clic de edad del rival titila entre el color del texto y el del
/// jugador, y es "mega molesto".
/// </summary>
public class RivalClickAlertTests
{
    private static readonly GameData Data = GameDataLoader.LoadLatest(Path.Combine(AppContext.BaseDirectory, "data", "game_data"));

    [Fact]
    public void El_clic_del_rival_alterna_su_color_con_el_del_texto()
    {
        var overlay = new OverlayViewModel(Data, "t") { Alert = "El rival hizo clic en Feudal", RivalSlot = 2 };
        var first = (overlay.AlertForeground, overlay.AlertBackground);

        overlay.Blink();

        Assert.True(overlay.RivalClick);
        Assert.NotEqual(first.AlertForeground, overlay.AlertForeground);
        Assert.NotEqual(first.AlertBackground, overlay.AlertBackground);
        overlay.Blink();
        Assert.Equal(first, (overlay.AlertForeground, overlay.AlertBackground));
    }

    [Fact]
    public void Los_demas_avisos_no_titilan()
    {
        var overlay = new OverlayViewModel(Data, "t") { Alert = "Se agotó una granja: resembrala" };
        var before = overlay.AlertForeground;

        overlay.Blink();

        Assert.False(overlay.RivalClick);
        Assert.Same(OverlayViewModel.AlertBrush, overlay.AlertForeground);
        Assert.Same(before, overlay.AlertForeground);
    }
}
