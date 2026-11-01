using BoCoach.Vision.Capture;
using BoCoach.Vision.Hud;
using BoCoach.Vision.Ocr;

namespace BoCoach.Vision.Tests;

/// <summary>
/// Panel de puntajes (D3). Recortes de la grabación 20261019-202635 (el usuario, azul, contra la IA, roja): a la
/// captura 660 los dos en la Edad Oscura; a la 900 el usuario ya llegó a Feudal; a la 1140, los dos.
/// </summary>
public class ScoresPanelTests
{
    private static CapturedImage Panel(string frame, string recording = "20261019-202635") =>
        Png.Read(Path.Combine(AppContext.BaseDirectory, "fixtures", "scores", $"{recording}-{frame}.png"));

    [Theory]
    [InlineData("UKyriokes: 779/779", 779)]
    [InlineData("UOtt0kar 1: 1100/1100 B%ll", 1100)]
    [InlineData("u Ottokar I.  455/455 Bo", 455)]
    [InlineData("914/914", null)] // sin nombre: el renglón cortado
    [InlineData("UKyriokes: 779/719", null)] // el primero pasa al más alto del equipo: lectura dudosa
    [InlineData("Otakar I: 1330/1343", 1330)] // 4 contra 4: el segundo es el más alto del equipo
    public void Lee_el_puntaje_del_renglon(string text, int? score) => Assert.Equal(score, ScoresPanelReader.Parse(text));

    [Theory]
    [InlineData("000660", 738, true, 632, true)]
    [InlineData("000900", 1064, false, 806, true)]
    [InlineData("001140", 1678, false, 1140, false)]
    public async Task Lee_cada_jugador_por_su_color_con_su_puntaje_y_su_edad(string frame, int mine, bool myDark, int rival, bool rivalDark)
    {
        var ocr = OcrReader.TryCreate();
        if (ocr is null)
            return; // esta máquina no tiene el OCR en inglés instalado (puede pasar en CI)
        var rows = await new ScoresPanelReader(ocr).ReadAsync(Panel(frame));

        var me = Assert.Single(rows, r => r.Slot == 1);
        var ai = Assert.Single(rows, r => r.Slot == 2);
        Assert.Equal((mine, myDark), (me.Score, me.DarkAge));
        Assert.Equal((rival, rivalDark), (ai.Score, ai.DarkAge));
    }

    // Partida 20261101-125353 (hunos contra mongoles): con el panel preparado en gris invertido entero, el OCR leía la
    // 1364 y no la 1368, 1372 ni 1376 (casi iguales), y la segunda galería (1464 → 1505) se perdía. Ahora va solo la
    // banda del panel, en negro sobre blanco.
    [Theory]
    [InlineData("001364", 1455, 1472)]
    [InlineData("001368", 1462, 1502)]
    [InlineData("001376", 1505, 1505)]
    [InlineData("002120", 4071, 2223)]
    public async Task Lee_el_panel_aunque_el_mapa_de_arriba_cambie(string frame, int mine, int rival)
    {
        var ocr = OcrReader.TryCreate();
        if (ocr is null)
            return;
        var rows = await new ScoresPanelReader(ocr).ReadAsync(Panel(frame, "20261101-125353"));

        Assert.Equal(mine, Assert.Single(rows, r => r.Slot == 1).Score);
        Assert.Equal(rival, Assert.Single(rows, r => r.Slot == 2).Score);
    }

    [Fact]
    public void La_banda_del_panel_es_la_parte_oscura_de_abajo()
    {
        var panel = Panel("001364", "20261101-125353");
        var top = ScoresPanelReader.PanelTop(panel);

        Assert.NotNull(top);
        Assert.InRange(top.Value, panel.Height - 60, panel.Height - 40);
    }
}
