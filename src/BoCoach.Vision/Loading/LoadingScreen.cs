using System.Globalization;
using System.Text;
using BoCoach.Core.Data;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Events;

namespace BoCoach.Vision.Loading;

/// <summary>Un jugador leído en la pantalla de carga.</summary>
/// <param name="Slot">Número de jugador (1 a 8), el renglón de la lista.</param>
/// <param name="Name">El nombre como lo leyó el OCR.</param>
/// <param name="CivText">La civ como la leyó el OCR.</param>
/// <param name="Civ">Clave de game_data de la civ ("incas"), o null si el texto no se parece a ninguna.</param>
/// <param name="Team">Equipo (1 a 4), o null si el OCR no leyó un número (sin equipo, "-", o no se entendió).</param>
/// <param name="Color">Color del jugador: el promedio del cuadrado de color (sin el número del medio).</param>
public sealed record LoadingPlayer(int Slot, string Name, string CivText, string? Civ, int? Team, (byte R, byte G, byte B) Color);

/// <summary>Lo leído en la pantalla de carga: los jugadores en el orden de la lista y el mapa.</summary>
public sealed record LoadingReading(IReadOnlyList<LoadingPlayer> Players, string? Map)
{
    /// <summary>
    /// El jugador con ese nombre (el que el usuario puso en la app), tolerando errores del OCR, o null. Si dos se
    /// parecen igual, ninguno: mejor no elegir que elegir mal.
    /// </summary>
    public LoadingPlayer? Find(string? playerName)
    {
        var wanted = LoadingScreen.NormalizeName(playerName ?? "");
        if (wanted.Length == 0)
            return null;
        var scored = Players
            .Select(p => (Player: p, Distance: EventMatcher.Levenshtein(LoadingScreen.NormalizeName(p.Name), wanted) / (double)wanted.Length))
            .Where(s => s.Distance <= LoadingScreen.MaxNameDistance)
            .OrderBy(s => s.Distance)
            .ToList();
        return scored.Count switch
        {
            0 => null,
            1 => scored[0].Player,
            _ => scored[1].Distance - scored[0].Distance >= 0.1 ? scored[0].Player : null,
        };
    }

    /// <summary>
    /// Los rivales de <paramref name="me"/>. Si se leyó algún equipo, los de otro equipo (y los que no tienen, que en
    /// un todos contra todos son rivales); si no se leyó ninguno, los de la otra paridad: según el usuario, en el
    /// juego los equipos son fijos, 1, 3, 5 y 7 contra 2, 4, 6 y 8.
    /// </summary>
    public IReadOnlyList<LoadingPlayer> Opponents(LoadingPlayer me) => Players.Any(p => p.Team is not null)
        ? Players.Where(p => p != me && (p.Team is null || p.Team != me.Team)).ToList()
        : Players.Where(p => p.Slot % 2 != me.Slot % 2).ToList();

    /// <summary>Los aliados de <paramref name="me"/> (sin contarlo), con la misma regla que <see cref="Opponents"/>.</summary>
    public IReadOnlyList<LoadingPlayer> Allies(LoadingPlayer me)
    {
        var opponents = Opponents(me);
        return Players.Where(p => p != me && !opponents.Contains(p)).ToList();
    }
}

/// <summary>
/// La pantalla de carga del juego ("Starting Game" / "Iniciando partida"), definida en
/// <c>widgetui/screenloadgametransition.json</c> a 3840×2160: una lista de hasta 8 jugadores (equipo, cuadrado de
/// color, nombre, ícono y nombre de la civ, un renglón cada 90 unidades) y a la derecha los ajustes de la partida
/// con el mapa. Las posiciones de acá salen de ese archivo. Suposición sin verificar: a otra resolución se escala
/// por el alto y, si la pantalla no es 16:9, se centra a lo ancho.
/// </summary>
public static class LoadingScreen
{
    public const double DesignWidth = 3840;
    public const double DesignHeight = 2160;
    public const int MaxPlayers = 8;

    /// <summary>Distancia relativa máxima entre un nombre leído y el buscado (errores / largo).</summary>
    public const double MaxNameDistance = 0.3;

    /// <summary>Distancia relativa máxima entre la civ leída y su nombre.</summary>
    public const double MaxCivDistance = 0.3;

    /// <summary>El título: textos 1109 del juego ("Starting Game"; en es y mx, "Iniciando partida").</summary>
    public static readonly IReadOnlyList<string> Titles = ["starting game", "iniciando partida"];

    /// <summary>El título de los ajustes, arriba del mapa: textos 200025 ("Game Settings", "Ajustes del juego").</summary>
    public static readonly IReadOnlyList<string> SettingsTitles = ["game settings", "ajustes del juego"];

    // Posiciones del diseño (unidades de 3840×2160). El panel de jugadores es un ancla en (700, 765); cada
    // renglón, otra en (10, 100 + 90 · (n - 1)) dentro del panel. Las cajas de texto van con un poco de margen.
    private static readonly (double X, double Y, double W, double H) TitleBox = (1420, 255, 1000, 100);
    private const double FirstRow = 865;
    private const double RowStep = 90;
    private const double RowHeight = 82;
    private static readonly (double X, double W) TeamColumn = (710, 82);
    private static readonly (double X, double W) ColorColumn = (802, 82);
    private static readonly (double X, double W) NameColumn = (915, 650);
    private static readonly (double X, double W) CivColumn = (1690, 340);
    // Los ajustes de la partida: el título ("Game Settings") y debajo, casi pegado, el mapa (LocationValue). Se leen
    // los dos renglones y el mapa es el que no es el título.
    private static readonly (double X, double Y, double W, double H) MapBox = (2295, 480, 724, 150);

    /// <summary>Un rectángulo del diseño llevado a una pantalla de <paramref name="width"/>×<paramref name="height"/>.</summary>
    public static PixelRect Place((double X, double Y, double W, double H) box, int width, int height)
    {
        var scale = height / DesignHeight;
        var offset = (width - DesignWidth * scale) / 2;
        static int Round(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);
        var x = Round(offset + box.X * scale);
        var y = Round(box.Y * scale);
        var right = Math.Min(width, Round(offset + (box.X + box.W) * scale));
        var bottom = Math.Min(height, Round((box.Y + box.H) * scale));
        x = Math.Max(0, x);
        y = Math.Max(0, y);
        return new PixelRect(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }

    public static PixelRect Title(int width, int height) => Place(TitleBox, width, height);

    public static PixelRect Map(int width, int height) => Place(MapBox, width, height);

    public static PixelRect Team(int slot, int width, int height) => Cell(TeamColumn, slot, width, height);

    public static PixelRect Color(int slot, int width, int height) => Cell(ColorColumn, slot, width, height);

    public static PixelRect Name(int slot, int width, int height) => Cell(NameColumn, slot, width, height);

    public static PixelRect Civ(int slot, int width, int height) => Cell(CivColumn, slot, width, height);

    private static PixelRect Cell((double X, double W) column, int slot, int width, int height) =>
        Place((column.X, FirstRow + RowStep * (slot - 1), column.W, RowHeight), width, height);

    /// <summary>Si el texto leído en la caja del título es el de la pantalla de carga.</summary>
    public static bool IsTitle(string? text) => Matches(text, Titles);

    /// <summary>El mapa entre los renglones leídos en su caja: el último que no es el título de los ajustes.</summary>
    public static string? MapFrom(IEnumerable<string> lines) =>
        lines.Select(l => l.Trim()).LastOrDefault(l => l.Length > 0 && !Matches(l, SettingsTitles));

    private static bool Matches(string? text, IReadOnlyList<string> phrases)
    {
        var normalized = Normalize(text ?? "");
        return normalized.Length >= 6 && phrases.Any(t =>
            EventMatcher.Levenshtein(normalized, t) / (double)t.Length <= 0.35 || normalized.Contains(t, StringComparison.Ordinal));
    }

    /// <summary>
    /// La civ cuyo nombre (en el idioma del juego, y si no en cualquiera) más se parece al texto, o null. Los nombres
    /// salen de game_data (<see cref="CivBonuses.Names"/>).
    /// </summary>
    public static string? MatchCiv(string? text, GameData data, string language)
    {
        var normalized = Normalize(text ?? "");
        if (normalized.Length < 2)
            return null;
        (string Key, double Distance)? best = null;
        foreach (var (key, civ) in data.Civs)
        {
            foreach (var (lang, name) in civ.Names)
            {
                var target = Normalize(name);
                if (target.Length == 0)
                    continue;
                // El nombre en otro idioma vale un poco menos: con el idioma mal elegido igual se reconoce.
                var distance = EventMatcher.Levenshtein(normalized, target) / (double)target.Length + (lang == language ? 0 : 0.05);
                if (best is null || distance < best.Value.Distance)
                    best = (key, distance);
            }
        }
        return best is { } b && b.Distance <= MaxCivDistance ? b.Key : null;
    }

    /// <summary>Como <see cref="EventMatcher.Normalize"/>, pero sin perder las letras con tilde ("Búlgaros" → "bulgaros").</summary>
    public static string Normalize(string text) => EventMatcher.Normalize(WithoutAccents(text));

    /// <summary>
    /// Un nombre de jugador para compararlo: minúsculas, sin tildes, solo letras y números, con las confusiones
    /// típicas del OCR unificadas (0 y o, 1 y l, 5 y s).
    /// </summary>
    public static string NormalizeName(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var raw in WithoutAccents(text).ToLowerInvariant())
        {
            var c = raw switch
            {
                '0' => 'o',
                '1' or '|' or '!' or 'i' => 'l',
                '5' or '$' => 's',
                _ => raw,
            };
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
                sb.Append(c);
        }
        return sb.ToString();
    }

    private static string WithoutAccents(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
