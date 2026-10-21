using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BoCoach.Core.Data;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Events;

namespace BoCoach.Vision.Loading;

/// <summary>Un jugador leído en la pantalla de carga.</summary>
/// <param name="Slot">Número de jugador (1 a 8), por el color de su cuadrado; si el color no se entiende, el orden en la lista.</param>
/// <param name="Name">El nombre como lo leyó el OCR.</param>
/// <param name="CivText">La civ como la leyó el OCR.</param>
/// <param name="Civ">Clave de game_data de la civ ("incas"), o null si el texto no se parece a ninguna.</param>
/// <param name="Team">Equipo, del encabezado "TEAM N" de arriba, o null si la lista no tiene equipos.</param>
/// <param name="Color">Color del cuadrado del jugador (sin el número del medio).</param>
public sealed record LoadingPlayer(int Slot, string Name, string CivText, string? Civ, int? Team, (byte R, byte G, byte B) Color);

/// <summary>Lo leído en la pantalla de carga: los jugadores en el orden de la lista.</summary>
/// <param name="Map">El mapa ("Arabia"), o null si no se leyó. El overlay del coach baja mientras no se ve el reloj para no taparlo.</param>
public sealed record LoadingReading(IReadOnlyList<LoadingPlayer> Players, string? Map = null)
{
    /// <summary>
    /// El jugador con ese nombre (el que el usuario puso en la app), tolerando errores del OCR ("Kvriokes"), o null.
    /// Si dos se parecen igual, ninguno: mejor no elegir que elegir mal.
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
    /// Los rivales de <paramref name="me"/>: los de otro equipo. Si la lista no tiene equipos, los de la otra paridad:
    /// en multijugador los equipos son 1, 3, 5 y 7 contra 2, 4, 6 y 8 (según el usuario; contra la IA, con equipos,
    /// la lista los separa).
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
/// La pantalla de carga del juego (verificada con una captura del usuario a 1920×1080, 2026-10-20): a la izquierda
/// la lista de jugadores, agrupada bajo "TEAM 1", "TEAM 2"... si hay equipos; cada jugador es su nombre y, debajo,
/// su civ en mayúsculas con un cuadrado del color del jugador a la derecha (con su número). Se lee con una sola
/// pasada del OCR sobre ese panel. Suposición sin verificar: a otra resolución se escala por el alto y, si la
/// pantalla no es 16:9, se centra a lo ancho.
/// </summary>
public static partial class LoadingScreen
{
    public const double ReferenceWidth = 1920;
    public const double ReferenceHeight = 1080;
    public const int MaxPlayers = 8;

    /// <summary>Distancia relativa máxima entre un nombre leído y el buscado (errores / largo).</summary>
    public const double MaxNameDistance = 0.3;

    /// <summary>Distancia relativa máxima entre la civ leída y su nombre.</summary>
    public const double MaxCivDistance = 0.3;

    /// <summary>
    /// El panel de jugadores a 1920×1080: desde el comienzo de las cajas de las civs hasta los cuadrados de color, y
    /// del encabezado del primer equipo al último jugador (8 jugadores con 2 encabezados llegan a y 870).
    /// </summary>
    private static readonly (double X, double Y, double W, double H) PanelBox = (170, 190, 335, 740);

    /// <summary>Alto del panel de jugadores a 1080p: el panel se lleva a este alto antes de leerlo.</summary>
    public static double PanelHeight => PanelBox.H;

    /// <summary>
    /// El mapa a 1920×1080: en el panel de la derecha, "GAME MODE: RANDOM MAP" y debajo el nombre del mapa en letras
    /// grandes ("ARABIA"). Medido en la captura del usuario, donde lo tapaba el overlay: sin verificar la lectura.
    /// </summary>
    private static readonly (double X, double Y, double W, double H) MapBox = (1385, 140, 505, 80);

    public static double MapHeight => MapBox.H;

    public static PixelRect Map(int width, int height) => Place(MapBox, width, height);

    /// <summary>El mapa entre los renglones de su caja: el último sin ":" (el de arriba es "GAME MODE: RANDOM MAP"), en "Arabia".</summary>
    public static string? MapFrom(IEnumerable<string> lines) =>
        lines.Select(l => l.Trim()).LastOrDefault(l => !l.Contains(':') && l.Count(char.IsLetter) >= 3) is { } map
            ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(map.ToLowerInvariant())
            : null;

    /// <summary>Columnas del cuadrado de color dentro del panel, a los costados del número (a 1080p, x 469 a 491).</summary>
    private static readonly (double From, double To)[] SquareColumns = [(300, 304), (316, 320)];

    /// <summary>Colores de los jugadores 1 a 8: azul, rojo, verde, amarillo, celeste, violeta, gris y naranja.</summary>
    private static readonly (double R, double G, double B)[] PlayerColors =
        [(0, 0, 1), (1, 0, 0), (0, 1, 0), (1, 1, 0), (0, 1, 1), (1, 0, 1), (1, 1, 1), (1, 0.65, 0)];

    /// <summary>Un rectángulo de la pantalla de 1920×1080 llevado a una de <paramref name="width"/>×<paramref name="height"/>.</summary>
    public static PixelRect Place((double X, double Y, double W, double H) box, int width, int height)
    {
        var scale = height / ReferenceHeight;
        var offset = (width - ReferenceWidth * scale) / 2;
        static int Round(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);
        var x = Math.Max(0, Round(offset + box.X * scale));
        var y = Math.Max(0, Round(box.Y * scale));
        var right = Math.Min(width, Round(offset + (box.X + box.W) * scale));
        var bottom = Math.Min(height, Round((box.Y + box.H) * scale));
        return new PixelRect(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }

    /// <summary>El panel de jugadores en una pantalla (el área del juego) de ese tamaño.</summary>
    public static PixelRect Panel(int width, int height) => Place(PanelBox, width, height);

    /// <summary>
    /// Arma la lista de jugadores con los renglones leídos en el panel (texto y altura del medio, en píxeles del panel
    /// llevado a 1080p): "TEAM N" fija el equipo, y cada renglón que es una civ cierra al jugador cuyo nombre es el
    /// renglón anterior. <paramref name="squareAt"/> da el color del cuadrado a esa altura.
    /// </summary>
    public static IReadOnlyList<LoadingPlayer> Parse(IEnumerable<(string Text, double Middle)> lines,
        Func<double, (byte R, byte G, byte B)> squareAt, GameData data, string language)
    {
        var players = new List<LoadingPlayer>();
        int? team = null;
        string? name = null;
        foreach (var (text, middle) in lines)
        {
            if (TeamOf(text) is { } header)
            {
                (team, name) = (header, null);
                continue;
            }
            var civText = CivPart(text);
            if (name is not null && MatchCiv(civText, data, language) is { } civ)
            {
                var color = squareAt(middle);
                var slot = SlotOf(color) is { } s && players.All(p => p.Slot != s) ? s : players.Count + 1;
                players.Add(new LoadingPlayer(slot, name, civText, civ, team, color));
                name = null;
                if (players.Count == MaxPlayers)
                    break;
                continue;
            }
            name = text.Trim();
        }
        return players;
    }

    /// <summary>"TEAM 2" (o "EQUIPO 2") → 2; el OCR a veces lee el 1 como "l" o "I".</summary>
    public static int? TeamOf(string text)
    {
        var match = TeamHeader().Match(text.Trim());
        if (!match.Success)
            return null;
        var digit = match.Groups["n"].Value;
        return digit is "l" or "I" or "|" ? 1 : int.Parse(digit, CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(@"^(?:team|equipo|eq\.?)\s*(?<n>[1-8lI|])$", RegexOptions.IgnoreCase)]
    private static partial Regex TeamHeader();

    /// <summary>La civ sin lo que el OCR pega del número del cuadrado ("BRITONS 3)" → "BRITONS").</summary>
    private static string CivPart(string text) =>
        string.Join(" ", text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Any(char.IsLetter))).Trim();

    /// <summary>El número de jugador por el color de su cuadrado, o null si no se parece a ninguno.</summary>
    public static int? SlotOf((byte R, byte G, byte B) color)
    {
        double max = Math.Max(color.R, Math.Max(color.G, color.B));
        if (max < 40)
            return null;
        var (r, g, b) = (color.R / max, color.G / max, color.B / max);
        var best = Enumerable.Range(0, PlayerColors.Length)
            .Select(i => (Slot: i + 1, Distance: Math.Abs(r - PlayerColors[i].R) + Math.Abs(g - PlayerColors[i].G) + Math.Abs(b - PlayerColors[i].B)))
            .MinBy(c => c.Distance);
        return best.Distance <= 0.5 ? best.Slot : null;
    }

    /// <summary>El color del cuadrado del jugador a la altura <paramref name="middle"/> del panel (a 1080p).</summary>
    public static (byte R, byte G, byte B) SquareColor(CapturedImage panel, double middle)
    {
        var scale = panel.Height / PanelBox.H;
        long r = 0, g = 0, b = 0, n = 0;
        var y0 = (int)Math.Round(middle * scale);
        for (var y = Math.Max(0, y0 - (int)(6 * scale)); y <= Math.Min(panel.Height - 1, y0 + (int)(6 * scale)); y++)
        {
            foreach (var (from, to) in SquareColumns)
            {
                for (var x = (int)(from * scale); x <= Math.Min(panel.Width - 1, (int)(to * scale)); x++)
                {
                    var p = panel.Pixel(x, y);
                    (r, g, b, n) = (r + p.R, g + p.G, b + p.B, n + 1);
                }
            }
        }
        return n == 0 ? ((byte)0, (byte)0, (byte)0) : ((byte)(r / n), (byte)(g / n), (byte)(b / n));
    }

    /// <summary>
    /// La civ cuyo nombre (en el idioma del juego, y si no en cualquiera) más se parece al texto, o null. Los nombres
    /// salen de game_data (<see cref="CivBonuses.Names"/>).
    /// </summary>
    public static string? MatchCiv(string? text, GameData data, string language)
    {
        var normalized = Normalize(text ?? "");
        if (normalized.Length < 3)
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
    /// típicas del OCR unificadas (0 y o, 1, i y l, 5 y s).
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
