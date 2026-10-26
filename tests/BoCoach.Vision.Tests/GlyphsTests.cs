using BoCoach.Vision.Capture;
using BoCoach.Vision.Digits;

namespace BoCoach.Vision.Tests;

public class GlyphsTests
{
    private static readonly InkSpec WhiteOnBlack = new() { Color = "white", Low = 50, High = 150 };

    /// <summary>Imagen negra de 24 filas con rectángulos (x, ancho, gris, de la fila `top` a la `bottom`).</summary>
    private static CapturedImage Image(int width, params (int X, int Width, byte Gray, int Top, int Bottom)[] blocks)
    {
        const int height = 24;
        var bgra = new byte[width * height * 4];
        foreach (var (x, w, gray, top, bottom) in blocks)
        {
            for (var y = top; y <= bottom; y++)
            {
                for (var xx = x; xx < x + w; xx++)
                {
                    var i = (y * width + xx) * 4;
                    bgra[i] = bgra[i + 1] = bgra[i + 2] = gray;
                    bgra[i + 3] = 255;
                }
            }
        }
        return new CapturedImage(width, height, bgra);
    }

    /// <summary>
    /// En el reloj del HUD propio el "0" y el ":" (o el "(" de "(Normal") se tocan por una columna de borde suavizado
    /// y se leían como un solo carácter (grabación 20261025-024356: "00:00:10" salía "00:00:(0" y el reloj se perdía en
    /// el 39 % de las capturas; "4/10" de población salía "430"). Un bloque más ancho que un dígito con una columna
    /// de poca tinta y sin píxeles firmes adentro se parte ahí.
    /// </summary>
    [Fact]
    public void Dos_caracteres_unidos_por_una_columna_tenue_se_separan_en_el_valle()
    {
        var image = Image(40, (2, 12, 255, 2, 21), (14, 1, 70, 2, 21), (15, 6, 255, 2, 21));

        var glyphs = Glyphs.Extract(image, WhiteOnBlack, 0.6);

        Assert.Equal(2, glyphs.Count);
        Assert.Equal((2, 13), (glyphs[0].Left, glyphs[0].Right));
        Assert.Equal((15, 20), (glyphs[1].Left, glyphs[1].Right));
    }

    [Fact]
    public void Un_caracter_ancho_sin_valle_no_se_parte()
    {
        var image = Image(40, (2, 16, 255, 2, 21));

        var glyphs = Glyphs.Extract(image, WhiteOnBlack, 0.6);

        Assert.Single(glyphs);
    }

    /// <summary>
    /// Dos dígitos pegados ("60", "90" en el stock de comida de la misma grabación) miden dos dígitos típicos. El hueco
    /// del "0" tiene poca tinta pero firme y en dos tramos (los arcos de arriba y de abajo) y puede tener menos que la
    /// columna donde se tocan: no es un valle, y el bloque se parte en partes iguales como siempre (partir en el hueco
    /// daba "6", "1", "1").
    /// </summary>
    [Fact]
    public void Dos_digitos_pegados_se_parten_al_medio_aunque_uno_tenga_un_hueco_con_poca_tinta()
    {
        var image = Image(40, (2, 7, 255, 2, 21), (9, 1, 255, 2, 3), (9, 1, 255, 20, 21), (10, 17, 255, 2, 21));

        var glyphs = Glyphs.Extract(image, WhiteOnBlack, 0.6);

        Assert.Equal(2, glyphs.Count);
        Assert.Equal((2, 13), (glyphs[0].Left, glyphs[0].Right));
        Assert.Equal((14, 26), (glyphs[1].Left, glyphs[1].Right));
    }

    /// <summary>Un reconocedor de prueba: su único carácter mide 6 columnas en la grilla; 10 de distancia por columna de diferencia.</summary>
    private static float SixColumns(float[] cell)
    {
        var inked = Enumerable.Range(0, GlyphFont.CellWidth)
            .Count(x => Enumerable.Range(0, GlyphFont.CellHeight).Any(y => cell[y * GlyphFont.CellWidth + x] > 0.3f));
        return Math.Abs(inked - 6) * 10f;
    }

    /// <summary>
    /// El "/" de "N/M" toca al "2" que sigue con trazo firme en la columna donde se juntan, como el hueco de un "0":
    /// por la tinta no se distinguen y "12/20" salía "12?0" (grabación 20261025-024356, desde los 4:00). Un bloque algo
    /// más ancho que un dígito que no se parece a ningún carácter se parte donde las dos partes sí se parecen.
    /// </summary>
    [Fact]
    public void Un_bloque_que_no_se_reconoce_se_parte_donde_las_dos_partes_se_reconocen()
    {
        var image = Image(40, (2, 18, 255, 2, 21));

        var glyphs = Glyphs.Extract(image, WhiteOnBlack, 0.6, SixColumns, 28);

        Assert.Equal(2, glyphs.Count);
        // Gana el corte que deja las dos partes más parecidas, no el primero que entra: 9 y 9.
        Assert.All(glyphs, g => Assert.InRange(g.Width, 8, 10));
        Assert.Equal(glyphs[0].Right + 1, glyphs[1].Left);
    }

    [Fact]
    public void Un_bloque_ancho_que_se_reconoce_entero_no_se_parte()
    {
        var image = Image(40, (2, 18, 255, 2, 21));

        var glyphs = Glyphs.Extract(image, WhiteOnBlack, 0.6, _ => 0, 28);

        Assert.Single(glyphs);
    }
}
