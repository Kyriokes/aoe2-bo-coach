using System.Text.Json;
using System.Text.Json.Serialization;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Regions;

namespace BoCoach.Vision.Hud;

/// <summary>Color del diseño, con cada canal de 0 a 1 (como lo escribe el juego en sus paneles).</summary>
public sealed record HudColor(double R, double G, double B)
{
    /// <summary>Lo que captura la pantalla: cada canal redondeado a 0..255.</summary>
    public (int R, int G, int B) Captured => (Channel(R), Channel(G), Channel(B));

    private static int Channel(double value) => (int)Math.Round(Math.Clamp(value, 0, 1) * 255);
}

/// <summary>Rectángulo en unidades del panel del juego.</summary>
public sealed record UnitRect(int X, int Y, int Width, int Height);

/// <summary>
/// Una pieza del panel dibujada con el marco del juego (<see cref="HudLayout.Frame"/>): su rectángulo en unidades del
/// HUD y en qué lados se ve el borde (los otros dan a otra pieza). <see cref="Box"/> es el Box del Surround, relativo
/// al ViewPort, con cada lado interior corrido <see cref="HudLayout.FrameClip"/> hacia afuera para que el juego recorte
/// esa fila del marco; <see cref="Base"/>, el fondo liso opaco de debajo, metido <see cref="HudLayout.Seam"/> en las
/// piezas vecinas.
/// </summary>
public sealed record FramePiece(string Name, UnitRect Rect, bool Top, bool Bottom, bool Left, bool Right)
{
    public UnitRect Box => Grown(HudLayout.FrameClip, relative: true);

    public UnitRect Base => Grown(HudLayout.Seam, relative: false);

    private UnitRect Grown(int by, bool relative)
    {
        var left = Left ? 0 : by;
        var top = Top ? 0 : by;
        var right = Right ? 0 : by;
        var bottom = Bottom ? 0 : by;
        return new UnitRect((relative ? 0 : Rect.X) - left, (relative ? 0 : Rect.Y) - top, Rect.Width + left + right, Rect.Height + top + bottom);
    }
}

public sealed record HudIcon(int X, int Y, int Size);

/// <summary>El casillero de color de una celda: ancla de la calibración y lugar de la GD (en población, el total de aldeanos).</summary>
public sealed record HudChip(int X, int Y, int Width, int Height, HudColor Color);

/// <summary>Un texto que el juego escribe y el lector lee: con qué letra (<see cref="Font"/>) y a qué tamaño lo escribe el mod.</summary>
public sealed record HudField(int X, int Y, int Width, int Height, string Font, int Points)
{
    /// <summary>Pegado a la izquierda del campo (diseño mínimo: los números junto al ícono) en vez de centrado.</summary>
    public bool AlignLeft { get; init; }
}

/// <summary>El "/" entre aldeanos y stock. Sin <see cref="Text"/>, una barrita del color.</summary>
public sealed record HudSeparator(int X, int Y, int Width, int Height, string? Text, int Points, HudColor Color);

/// <summary>Una celda de la barra, con todo adentro en coordenadas relativas a ella (el alto es el de la barra).</summary>
public sealed record HudCell(
    string Name,
    int X,
    int Width,
    HudIcon Icon,
    HudChip? Chip,
    IReadOnlyDictionary<string, HudField> Fields,
    IReadOnlyList<UnitRect> Backgrounds,
    HudSeparator? Separator)
{
    /// <summary>
    /// Dónde escribe el overlay la GD (diseño mínimo, 2026-11-01): un hueco que el mod deja libre a la derecha de los
    /// aldeanos, donde va la diferencia con lo que pide el reparto ("=6", "+2", "-5"). Null: la GD va dentro del
    /// casillero (diseño completo) o debajo de la caja (sin diseño).
    /// </summary>
    public UnitRect? Guide { get; init; }
}

/// <param name="Width">Hasta dónde llega la barra, desde el borde izquierdo del panel.</param>
public sealed record HudBar(int Width, int Height, HudColor Background, int BorderWidth, HudColor BorderColor)
{
    /// <summary>Dónde empieza la barra (a la derecha del log, si el diseño lo pone en el rincón).</summary>
    public int X { get; init; }
}

/// <summary>El reloj va en otro panel del juego: a <see cref="Gap"/> unidades del final de la última celda de la barra.</summary>
public sealed record HudClock(int Gap, int Y, int Width, int Height, string Font, int Points);

/// <summary>
/// El diseño del panel de recursos del mod BoCoach HUD (data/hud_layout.json), en unidades del panel del juego (el
/// ícono original mide 84). De acá salen el mod que arma la app, la calibración sola (los casilleros de color son las
/// anclas), el lector (cada campo se recorta en proporción dentro de la caja de su celda) y el lugar de la GD.
/// </summary>
public sealed class HudLayout
{
    public const string FileName = "hud_layout.json";

    /// <summary>El diseño mínimo (la barra del juego opaca con los números blancos y nada más), al lado del completo.</summary>
    public const string MinimalFileName = "hud_layout_minimo.json";

    /// <summary>Las celdas, en el orden en que van en la barra.</summary>
    public static readonly string[] CellOrder = ["wood", "food", "gold", "stone", "population", "idle", "age"];

    private static readonly Dictionary<string, string[]> FieldsOf = new()
    {
        ["wood"] = ["villagers", "stock"],
        ["food"] = ["villagers", "stock"],
        ["gold"] = ["villagers", "stock"],
        ["stone"] = ["villagers", "stock"],
        ["population"] = ["total", "cap"],
        ["idle"] = ["count"],
        ["age"] = ["text"],
    };

    private static readonly string[] Fonts = ["hud_numbers", "mod_clock", "ocr"];

    private static readonly Dictionary<string, string> Regions = new()
    {
        ["wood"] = RegionNames.Wood,
        ["food"] = RegionNames.Food,
        ["gold"] = RegionNames.Gold,
        ["stone"] = RegionNames.Stone,
        ["population"] = RegionNames.Population,
        ["idle"] = RegionNames.IdleVillagers,
        ["age"] = RegionNames.Age,
    };

    private HudLayout(string name, double pixelsPerUnitAt1080p, HudBar bar, IReadOnlyList<HudCell> cells, HudClock clock)
    {
        Name = name;
        PixelsPerUnitAt1080p = pixelsPerUnitAt1080p;
        Bar = bar;
        Cells = cells;
        Clock = clock;
    }

    public string Name { get; }

    /// <summary>Píxeles por unidad a 1920×1080 con la escala de HUD de referencia (84 unidades = 36 px).</summary>
    public double PixelsPerUnitAt1080p { get; }

    public HudBar Bar { get; }

    /// <summary>En el orden de <see cref="CellOrder"/>.</summary>
    public IReadOnlyList<HudCell> Cells { get; }

    public HudClock Clock { get; }

    /// <summary>
    /// El log de eventos del juego, en el rincón de arriba a la izquierda sobre el gris de la barra (idea 1 del usuario,
    /// 2026-10-31: sobre el mapa, la pausa y los colores del terreno ensuciaban el OCR; el negro abajo de la barra se veía
    /// feo). Null en los diseños viejos: el log queda donde lo pone el juego y su caja es la de la calibración.
    /// </summary>
    public UnitRect? Log { get; private init; }

    /// <summary>
    /// La fila de íconos de la cola de producción, debajo de la barra; el mod pone el gris de la barra alrededor (sobre
    /// el mapa, los íconos verdes se confundían con el pasto). Null en los diseños viejos.
    /// </summary>
    public UnitRect? Queue { get; private init; }

    /// <summary>
    /// Escala del menú del juego (escudo y botones de arriba a la derecha), pegado al borde derecho: más chico deja ver
    /// más texto del reloj (pedido del usuario, 2026-11-01: "(Normal - 1.7)" quedaba detrás de los botones). Null: el
    /// menú queda como lo trae el juego.
    /// </summary>
    public double? MenuScale { get; private init; }

    /// <summary>Margen del fondo de la cola alrededor de sus íconos, en unidades del HUD.</summary>
    public const int QueueMargin = 6;

    /// <summary>
    /// Radio de las esquinas del panel que dan al mapa, en unidades del HUD, hechas con escalones. 0: esquinas rectas
    /// (con <see cref="Frame"/>, las esquinas las trae la textura del juego).
    /// </summary>
    public int CornerRadius { get; private init; }

    /// <summary>
    /// Material de 9 piezas del juego con que el mod dibuja el marco del panel (<c>BlackPanel</c>: el del log, los
    /// puntajes y los objetivos; un marco fino claro con fondo oscuro translúcido). Pedido del usuario del 2026-11-01:
    /// un borde a tono con el juego en vez de un corte gris. Pide <see cref="SinglePanel"/>; las piezas salen de
    /// <see cref="FramePieces"/>. Null: rectángulos lisos del color de la barra.
    /// </summary>
    public string? Frame { get; private init; }

    /// <summary>
    /// Una fila de piezas del marco, en unidades del HUD (el <c>gridstep</c> de los Surround del juego). El Box de un
    /// Surround se dibuja desde el origen de su ViewPort y el juego recorta lo que se sale (prueba de la v0.9.74,
    /// 2026-11-01): por cada lado de una pieza que da a otra pieza, el Box se corre esto hacia afuera y esa fila del
    /// marco queda recortada, así el borde se ve solo hacia el mapa.
    /// </summary>
    public const int FrameClip = 32;

    /// <summary>
    /// Cuánto se mete el fondo liso opaco de cada pieza en las piezas vecinas, para que entre dos fondos no quede
    /// ninguna línea del mapa.
    /// </summary>
    public const int Seam = 4;

    /// <summary>
    /// El fondo de la cola. Con el log en el rincón, un solo panel con el log y la barra: va desde el final de la barra
    /// (bar.x, su alto) hasta el final de la fila de íconos con su margen, tocando el log y la barra. Si no, la fila de
    /// íconos con <see cref="QueueMargin"/> alrededor. Null sin cola.
    /// </summary>
    public UnitRect? QueueBack
    {
        get
        {
            if (Queue is not { } q)
                return null;
            var m = QueueMargin;
            if (!SinglePanel)
                return new UnitRect(q.X - m, q.Y - m, q.Width + 2 * m, q.Height + 2 * m);
            return new UnitRect(Bar.X, Bar.Height, q.X + q.Width + m - Bar.X, q.Y + q.Height + m - Bar.Height);
        }
    }

    /// <summary>El log en el rincón a la izquierda de la barra y la cola debajo: los tres forman un solo panel.</summary>
    public bool SinglePanel => Log is { } log && Queue is not null && log.X + log.Width <= Bar.X;

    /// <summary>
    /// Las piezas con que el mod dibuja el panel con el marco del juego (<see cref="Frame"/>), en unidades del HUD: el
    /// log hasta donde termina la cola (borde arriba y a la izquierda) y el resto del log (izquierda, abajo y derecha:
    /// el escalón), la barra sobre la cola (solo arriba) y desde el final de la cola hasta el borde (arriba, abajo y
    /// derecha), la cola (abajo y derecha) y el campo del reloj, del alto de la barra (arriba y abajo: las mismas líneas
    /// de la barra, por si su panel se dibuja encima). Vacía sin <see cref="Frame"/>.
    /// </summary>
    public IReadOnlyList<FramePiece> FramePieces()
    {
        if (Frame is null)
            return [];
        if (Log is not { } log || QueueBack is not { } queue)
        {
            // Diseño mínimo: la barra sola, de borde a borde (el marco arriba y a los costados cae fuera de la pantalla),
            // y el campo del reloj con las mismas líneas de arriba y de abajo.
            var clockOnly = ClockRect();
            return
            [
                new("Bar", new UnitRect(Bar.X, 0, Bar.Width - Bar.X, Bar.Height), Top: true, Bottom: true, Left: true, Right: true),
                new("Clock", new UnitRect(clockOnly.X, 0, clockOnly.Width, Bar.Height), Top: true, Bottom: true, Left: false, Right: false),
            ];
        }
        var logBottom = log.Y + log.Height;
        var step = Math.Min(queue.Y + queue.Height, logBottom);
        var pieces = new List<FramePiece>
        {
            new("LogTop", new UnitRect(log.X, log.Y, log.Width, step - log.Y), Top: true, Bottom: step >= logBottom, Left: true, Right: false),
        };
        if (step < logBottom)
            pieces.Add(new("LogBottom", new UnitRect(log.X, step, log.Width, logBottom - step), Top: false, Bottom: true, Left: true, Right: true));
        var queueRight = queue.X + queue.Width;
        pieces.Add(new("BarLeft", new UnitRect(Bar.X, 0, queueRight - Bar.X, Bar.Height), Top: true, Bottom: false, Left: false, Right: false));
        pieces.Add(new("BarRight", new UnitRect(queueRight, 0, Bar.Width - queueRight, Bar.Height), Top: true, Bottom: true, Left: false, Right: true));
        pieces.Add(new("Queue", queue, Top: false, Bottom: true, Left: false, Right: true));
        var clock = ClockRect();
        pieces.Add(new("Clock", new UnitRect(clock.X, 0, clock.Width, Bar.Height), Top: true, Bottom: true, Left: false, Right: false));
        return pieces;
    }

    /// <summary>
    /// Un rectángulo con la esquina de abajo a la derecha redondeada, hecho con rectángulos (el juego solo dibuja
    /// rectángulos): el cuerpo sin la franja de abajo y, en esa franja, tiras de <paramref name="step"/> de alto cada
    /// vez más cortas, siguiendo un cuarto de círculo de radio <paramref name="radius"/>.
    /// </summary>
    public static IReadOnlyList<UnitRect> RoundedBottomRight(UnitRect rect, int radius, int step = 2)
    {
        var r = Math.Min(radius, Math.Min(rect.Width, rect.Height));
        if (r <= 0)
            return [rect];
        var parts = new List<UnitRect> { rect with { Height = rect.Height - r } };
        for (var y = 0; y < r; y += step)
        {
            var height = Math.Min(step, r - y);
            var t = y + height / 2.0; // distancia al centro del círculo, hacia abajo
            var inset = (int)Math.Round(r - Math.Sqrt(Math.Max(0, (double)r * r - t * t)));
            parts.Add(new UnitRect(rect.X, rect.Y + rect.Height - r + y, rect.Width - inset, height));
        }
        return parts;
    }

    public HudCell Cell(string name) => Cells.First(c => c.Name == name);

    /// <summary>Las celdas con casillero, en orden: las anclas de la calibración.</summary>
    public IEnumerable<HudCell> Anchors => Cells.Where(c => c.Chip is not null);

    public static string RegionOf(string cell) => Regions[cell];

    public static string? CellOf(string region) => Regions.FirstOrDefault(r => r.Value == region).Key;

    public UnitRect CellRect(HudCell cell) => new(cell.X, 0, cell.Width, Bar.Height);

    /// <summary>El campo de texto de la edad, en coordenadas de la barra.</summary>
    public UnitRect AgeTextRect()
    {
        var age = Cell("age");
        var text = age.Fields["text"];
        return new UnitRect(age.X + text.X, text.Y, text.Width, text.Height);
    }

    /// <summary>
    /// El rectángulo negro del reloj, en coordenadas de la barra: a <see cref="HudClock.Gap"/> del final de la última
    /// celda (la barra sigue hasta el borde de la pantalla).
    /// </summary>
    public UnitRect ClockRect()
    {
        var last = Cells[^1];
        return new UnitRect(last.X + last.Width + Clock.Gap, Clock.Y, Clock.Width, Clock.Height);
    }

    /// <summary>Dónde cae un campo dentro de la caja de su celda, en proporción.</summary>
    public PixelRect FieldRect(HudCell cell, string field, PixelRect box)
    {
        var f = cell.Fields[field];
        return Proportional(cell, box, f.X, f.Y, f.Width, f.Height);
    }

    /// <summary>Dónde cae la guía (<see cref="HudCell.Guide"/>) dentro de la caja de su celda, en proporción; null si la celda no tiene.</summary>
    public PixelRect? GuideRect(HudCell cell, PixelRect box) =>
        cell.Guide is { } g ? Proportional(cell, box, g.X, g.Y, g.Width, g.Height) : null;

    /// <summary>Dónde cae el casillero dentro de la caja de su celda, en proporción.</summary>
    public PixelRect ChipRect(HudCell cell, PixelRect box)
    {
        var chip = cell.Chip ?? throw new ArgumentException($"La celda {cell.Name} no tiene casillero.");
        return Proportional(cell, box, chip.X, chip.Y, chip.Width, chip.Height);
    }

    private PixelRect Proportional(HudCell cell, PixelRect box, int x, int y, int width, int height) => new(
        box.X + Scale(x, box.Width, cell.Width),
        box.Y + Scale(y, box.Height, Bar.Height),
        Scale(width, box.Width, cell.Width),
        Scale(height, box.Height, Bar.Height));

    private static int Scale(int units, int pixels, int totalUnits) => (int)Math.Round((double)units * pixels / totalUnits);

    public static HudLayout Load(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path), Path.GetFileName(path));
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"{Path.GetFileName(path)} está mal formado: {e.Message}", e);
        }
    }

    public static HudLayout Parse(string json, string source = FileName)
    {
        var dto = JsonSerializer.Deserialize<Dto>(json, JsonOptions) ?? throw new InvalidDataException($"{source} está vacío.");
        string Missing(string what) => $"{source}: falta {what}.";
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidDataException(Missing("name"));
        if (dto.PixelsPerUnitAt1080p is not > 0)
            throw new InvalidDataException(Missing("pixels_per_unit_at_1080p"));
        var barDto = dto.Bar ?? throw new InvalidDataException(Missing("bar"));
        var bar = new HudBar(barDto.Width, barDto.Height, Color(barDto.Background, "bar.background", source),
            barDto.Border?.Width ?? 0, Color(barDto.Border?.Color, "bar.border.color", source)) { X = barDto.X };
        var clockDto = dto.Clock ?? throw new InvalidDataException(Missing("clock"));
        var clock = new HudClock(clockDto.Gap, clockDto.Y, clockDto.Width, clockDto.Height, clockDto.Font ?? "", clockDto.Points);
        if (!Fonts.Contains(clock.Font))
            throw new InvalidDataException($"{source}: la letra del reloj \"{clock.Font}\" no existe (vale {string.Join(", ", Fonts)}).");

        var cells = new List<HudCell>();
        foreach (var name in CellOrder)
        {
            if (dto.Cells is null || !dto.Cells.TryGetValue(name, out var c))
                throw new InvalidDataException(Missing($"la celda {name}"));
            var icon = c.Icon ?? throw new InvalidDataException(Missing($"el ícono de {name}"));
            var fields = new Dictionary<string, HudField>();
            foreach (var field in FieldsOf[name])
            {
                if (c.Fields is null || !c.Fields.TryGetValue(field, out var f))
                    throw new InvalidDataException(Missing($"el campo {field} de {name}"));
                if (!Fonts.Contains(f.Font ?? ""))
                    throw new InvalidDataException($"{source}: la letra \"{f.Font}\" del campo {field} de {name} no existe (vale {string.Join(", ", Fonts)}).");
                var align = (f.Align ?? "centre").Trim().ToLowerInvariant();
                if (align is not ("centre" or "center" or "left"))
                    throw new InvalidDataException($"{source}: align del campo {field} de {name} tiene que ser \"left\" o \"centre\".");
                fields[field] = new HudField(f.X, f.Y, f.Width, f.Height, f.Font!, f.Points) { AlignLeft = align == "left" };
            }
            foreach (var extra in (c.Fields?.Keys ?? Enumerable.Empty<string>()).Except(FieldsOf[name]))
                throw new InvalidDataException($"{source}: la celda {name} no tiene un campo \"{extra}\" (tiene {string.Join(", ", FieldsOf[name])}).");
            HudChip? chip = c.Chip is { } ch ? new HudChip(ch.X, ch.Y, ch.Width, ch.Height, Color(ch.Color, $"{name}.chip.color", source)) : null;
            if (chip is null && name is not ("idle" or "age"))
                throw new InvalidDataException(Missing($"el casillero de {name}"));
            var backgrounds = (c.Backgrounds ?? []).Select(b => new UnitRect(b.X, b.Y, b.Width, b.Height)).ToList();
            HudSeparator? separator = c.Separator is { } s
                ? new HudSeparator(s.X, s.Y, s.Width, s.Height, s.Text, s.Points, Color(s.Color, $"{name}.separator.color", source))
                : null;
            cells.Add(new HudCell(name, c.X, c.Width, new HudIcon(icon.X, icon.Y, icon.Size), chip, fields, backgrounds, separator)
            {
                Guide = c.Guide is { } g ? new UnitRect(g.X, g.Y, g.Width, g.Height) : null,
            });
        }
        UnitRect? Rect(RectDto? r, string what) => r is null ? null
            : r.Width > 0 && r.Height > 0 && r.X >= 0 && r.Y >= 0 && (r.Y >= bar.Height || r.X + r.Width <= bar.X) ? new UnitRect(r.X, r.Y, r.Width, r.Height)
            : throw new InvalidDataException($"{source}: {what} tiene que medir más que 0 y quedar abajo de la barra (y {bar.Height} o más) o a su izquierda (hasta x {bar.X}).");
        var layout = new HudLayout(dto.Name, dto.PixelsPerUnitAt1080p.Value, bar, cells, clock)
        {
            Log = Rect(dto.Log, "log"),
            Queue = Rect(dto.Queue, "queue"),
            Frame = string.IsNullOrWhiteSpace(dto.Frame) ? null : dto.Frame.Trim(),
            CornerRadius = dto.CornerRadius is { } radius
                ? radius >= 0 ? radius : throw new InvalidDataException($"{source}: corner_radius no puede ser negativo.")
                : 0,
            MenuScale = dto.Menu?.Scale is { } scale
                ? scale is > 0.3 and <= 1 ? scale : throw new InvalidDataException($"{source}: menu.scale tiene que ir de 0,3 a 1.")
                : null,
        };
        // El marco va sobre un solo panel: el log en el rincón con la cola debajo (diseño completo) o la barra sola
        // (diseño mínimo, sin log ni cola corridos).
        if (layout.Frame is not null && !layout.SinglePanel && (layout.Log is not null || layout.Queue is not null))
            throw new InvalidDataException($"{source}: frame pide log a la izquierda de la barra y queue debajo (un solo panel), o ninguno de los dos.");
        layout.Validate(source);
        return layout;
    }

    private void Validate(string source)
    {
        HudCell? previous = null;
        foreach (var cell in Cells)
        {
            if (cell.Width <= 0 || cell.X < Bar.X || cell.X + cell.Width > Bar.Width)
                throw new InvalidDataException($"{source}: la celda {cell.Name} se sale de la barra.");
            if (previous is not null && cell.X < previous.X + previous.Width)
                throw new InvalidDataException($"{source}: la celda {cell.Name} se solapa con {previous.Name}.");
            void Inside(string what, int x, int y, int w, int h)
            {
                if (w <= 0 || h <= 0 || x < 0 || y < 0 || x + w > cell.Width || y + h > Bar.Height)
                    throw new InvalidDataException($"{source}: {what} de {cell.Name} se sale de la celda.");
            }
            Inside("el ícono", cell.Icon.X, cell.Icon.Y, cell.Icon.Size, cell.Icon.Size);
            if (cell.Chip is { } chip)
                Inside("el casillero", chip.X, chip.Y, chip.Width, chip.Height);
            foreach (var (name, f) in cell.Fields)
                Inside($"el campo {name}", f.X, f.Y, f.Width, f.Height);
            foreach (var b in cell.Backgrounds)
                Inside("un fondo", b.X, b.Y, b.Width, b.Height);
            if (cell.Separator is { } s)
                Inside("el separador", s.X, s.Y, s.Width, s.Height);
            if (cell.Guide is { } g)
                Inside("la guía", g.X, g.Y, g.Width, g.Height);
            previous = cell;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(new Dto(
        Name,
        PixelsPerUnitAt1080p,
        new BarDto(Bar.Width, Bar.Height, Array(Bar.Background), new BorderDto(Bar.BorderWidth, Array(Bar.BorderColor)), Bar.X),
        Cells.ToDictionary(c => c.Name, c => new CellDto(
            c.X,
            c.Width,
            new IconDto(c.Icon.X, c.Icon.Y, c.Icon.Size),
            c.Chip is { } chip ? new ChipDto(chip.X, chip.Y, chip.Width, chip.Height, Array(chip.Color)) : null,
            c.Fields.ToDictionary(f => f.Key, f => new FieldDto(f.Value.X, f.Value.Y, f.Value.Width, f.Value.Height, f.Value.Font, f.Value.Points, f.Value.AlignLeft ? "left" : null)),
            c.Backgrounds.Select(b => new RectDto(b.X, b.Y, b.Width, b.Height)).ToList(),
            c.Separator is { } s ? new SeparatorDto(s.X, s.Y, s.Width, s.Height, s.Text, s.Points, Array(s.Color)) : null,
            c.Guide is { } g ? new RectDto(g.X, g.Y, g.Width, g.Height) : null)),
        new ClockDto(Clock.Gap, Clock.Y, Clock.Width, Clock.Height, Clock.Font, Clock.Points),
        Log is { } log ? new RectDto(log.X, log.Y, log.Width, log.Height) : null,
        Queue is { } queue ? new RectDto(queue.X, queue.Y, queue.Width, queue.Height) : null,
        MenuScale is { } menu ? new MenuDto(menu) : null,
        CornerRadius > 0 ? CornerRadius : null,
        Frame), JsonOptions);

    private static HudColor Color(double[]? values, string what, string source) =>
        values is { Length: 3 } ? new HudColor(values[0], values[1], values[2]) : throw new InvalidDataException($"{source}: {what} tiene que ser [r, g, b] de 0 a 1.");

    private static double[] Array(HudColor color) => [color.R, color.G, color.B];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed record Dto(string? Name, [property: JsonPropertyName("pixels_per_unit_at_1080p")] double? PixelsPerUnitAt1080p, BarDto? Bar, Dictionary<string, CellDto>? Cells, ClockDto? Clock,
        RectDto? Log = null, RectDto? Queue = null, MenuDto? Menu = null, int? CornerRadius = null, string? Frame = null);
    private sealed record MenuDto(double? Scale);
    private sealed record BarDto(int Width, int Height, double[]? Background, BorderDto? Border, int X = 0);
    private sealed record BorderDto(int Width, double[]? Color);
    private sealed record IconDto(int X, int Y, int Size);
    private sealed record ChipDto(int X, int Y, int Width, int Height, double[]? Color);
    private sealed record FieldDto(int X, int Y, int Width, int Height, string? Font, int Points, string? Align = null);
    private sealed record RectDto(int X, int Y, int Width, int Height);
    private sealed record SeparatorDto(int X, int Y, int Width, int Height, string? Text, int Points, double[]? Color);
    private sealed record CellDto(int X, int Width, IconDto? Icon, ChipDto? Chip, Dictionary<string, FieldDto>? Fields, List<RectDto>? Backgrounds, SeparatorDto? Separator, RectDto? Guide = null);
    private sealed record ClockDto(int Gap, int Y, int Width, int Height, string? Font, int Points);
}
