using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BoCoach.Vision.Hud;

namespace BoCoach.App.Capture;

/// <summary>Lo que dejó la instalación del mod: un renglón por perfil del juego y los avisos.</summary>
public sealed record HudModResult(IReadOnlyList<string> Installed, IReadOnlyList<string> Warnings)
{
    public bool Ok => Installed.Count > 0;
}

/// <summary>
/// El mod local "BoCoach HUD": el panel de recursos y el del reloj armados desde los originales del juego y el diseño
/// (data/hud_layout.json). Cada widget del juego se conserva con su nombre, su tipo y su tooltip (así el juego sigue
/// escribiendo los números donde corresponde) y solo se reubica según el diseño:
/// <list type="bullet">
/// <item>resourcepanel.json: la barra lisa del color del diseño en los 9 estados de civ, con un borde fino; cada celda de
/// recurso es un contenedor transparente con el ícono del juego, el casillero de color (la GD de la app), los fondos
/// negros y los números blancos grandes; población, inactivos y edad igual.</item>
/// <item>worldtimerpanel.json: el reloj a continuación de la barra, grande y con un rectángulo negro detrás.</item>
/// <item>notificationpanel.json y technologyprogresspanel.json (si el diseño tiene <c>log</c> y <c>queue</c>): el log de
/// eventos en el rincón de arriba a la izquierda (la barra empieza a su derecha) y la cola de producción debajo de la
/// barra, los dos sobre el gris de la barra (idea 1 del usuario, 2026-10-31).</item>
/// </list>
/// Los paneles se arman en la PC del jugador: no se redistribuye el JSON del juego.
/// </summary>
public static class HudMod
{
    public const string Name = "BoCoach HUD";

    /// <summary>Donde el juego guarda un perfil por cuenta de Steam (cada uno con su carpeta mods).</summary>
    public static string DefaultProfilesRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games", "Age of Empires 2 DE");

    private const string ResourcePanelFile = "resourcepanel.json";
    private const string ClockPanelFile = "worldtimerpanel.json";
    private const string LogPanelFile = "notificationpanel.json";
    private const string QueuePanelFile = "technologyprogresspanel.json";

    /// <summary>
    /// El origen de las colecciones del log y del reloj se mide en unidades de pantalla (3840 de ancho, 0,5 px a 1080p);
    /// los widgets de adentro, como el diseño, en las del HUD (<see cref="HudLayout.PixelsPerUnitAt1080p"/> px a
    /// 1080p). Primera prueba del log (2026-10-31, grabación 20261031-215314): con el tamaño pasado a unidades de
    /// pantalla, el fondo de 600×360 midió 260×157 px (0,4348 px por unidad) y no los 304×183 del diseño.
    /// </summary>
    private const double PixelsPerScreenUnitAt1080p = 0.5;

    /// <summary>Margen del texto del log dentro de su fondo, en unidades del HUD (el del juego).</summary>
    private const int LogTextMargin = 10;

    /// <summary>Margen izquierdo del texto del reloj dentro de su campo negro, en unidades del panel.</summary>
    public const int ClockTextX = 20;

    /// <summary>Lo que agrega el mod lleva este prefijo: se saca antes de volver a armar el panel.</summary>
    private const string Ours = "BoCoach";

    private static readonly string[] Containers = ["Wood", "Food", "Gold", "Stone", "Population"];

    private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// Arma el mod en cada perfil del juego que tenga carpeta mods. <paramref name="game"/> es la carpeta del juego (de
    /// ahí salen los paneles originales).
    /// </summary>
    public static HudModResult Install(string game, HudLayout layout, string? profilesRoot = null)
    {
        var installed = new List<string>();
        var warnings = new List<string>();
        profilesRoot ??= DefaultProfilesRoot;
        var originals = Path.Combine(game, "widgetui");
        if (!File.Exists(Path.Combine(originals, ResourcePanelFile)) || !File.Exists(Path.Combine(originals, ClockPanelFile)))
        {
            warnings.Add($"No encontré los paneles del juego en {game}.");
            return new HudModResult(installed, warnings);
        }
        if (!Directory.Exists(profilesRoot))
        {
            warnings.Add($"No encontré los perfiles del juego en {profilesRoot}: abrí el juego una vez y volvé a probar.");
            return new HudModResult(installed, warnings);
        }

        foreach (var profile in Directory.EnumerateDirectories(profilesRoot).Order())
        {
            var mods = Path.Combine(profile, "mods");
            if (!Directory.Exists(mods))
                continue;
            var status = ReadStatus(mods);
            var active = status.Where(m => m.Enabled && !IsOurs(m)).OrderBy(m => m.Priority).ToList();

            var target = Path.Combine(mods, "local", Name);
            var own = Path.Combine(target, "widgetui");
            var resources = ResourcePanel(Read(Path.Combine(originals, ResourcePanelFile)), layout);
            var clock = ClockPanel(Read(Path.Combine(originals, ClockPanelFile)), layout);
            Directory.CreateDirectory(own);
            Write(Path.Combine(own, ResourcePanelFile), resources);
            Write(Path.Combine(own, ClockPanelFile), clock);
            // El log y la cola, solo si el diseño los ubica y el juego trae el panel; si no, se borra la copia de antes.
            foreach (var (file, build) in new (string, Func<JsonObject, HudLayout, JsonObject?>)[] { (LogPanelFile, LogPanel), (QueuePanelFile, QueuePanel) })
            {
                var original = Path.Combine(originals, file);
                var panel = File.Exists(original) ? build(Read(original), layout) : null;
                if (panel is not null)
                    Write(Path.Combine(own, file), panel);
                else if (File.Exists(Path.Combine(own, file)))
                    File.Delete(Path.Combine(own, file));
            }
            var info = new JsonObject
            {
                ["Author"] = "BoCoach",
                ["CacheStatus"] = 0,
                ["Description"] = $"Panel de recursos propio ({layout.Name}): fondo liso, íconos del juego, casillero del coach y campos negros para que BoCoach los lea.",
                ["Title"] = Name,
            };
            Write(Path.Combine(target, "info.json"), info);

            var name = Path.GetFileName(profile);
            installed.Add($"Perfil {name}: diseño {layout.Name}.");

            // Otro mod activo que cambia los mismos paneles y tiene más prioridad (número más bajo) le gana al nuestro.
            var ours = status.FirstOrDefault(IsOurs);
            if (ours is { Enabled: false })
                warnings.Add($"Perfil {name}: \"{Name}\" está apagado en Mods instalados. Activalo.");
            foreach (var mod in active.Where(m => ours is null || m.Priority < ours.Priority))
            {
                if (TouchesPanels(mods, mod))
                    warnings.Add($"Perfil {name}: \"{mod.Title}\" también cambia el panel de recursos, el reloj, el log o la cola"
                        + (ours is null ? "." : $" y tiene más prioridad ({mod.Priority} contra {ours.Priority}).")
                        + $" Poné \"{Name}\" primero en Mods instalados o apagá ese mod.");
            }
        }
        if (installed.Count == 0)
            warnings.Add("Ningún perfil del juego tiene carpeta de mods: abrí el juego, entrá una vez a Mods y volvé a probar.");
        return new HudModResult(installed, warnings);
    }

    /// <summary>El panel de recursos armado según el diseño a partir del original del juego (se modifica y se devuelve).</summary>
    public static JsonObject ResourcePanel(JsonObject original, HudLayout layout)
    {
        var bar = layout.Bar;
        var collection = original["Collection"]!.AsObject();
        collection["ViewPort"] = View(0, 0, bar.Width, bar.Height);
        var background = collection["Widgets"]!.AsArray()[0]!["Widget"]!.AsObject();
        StripOurs(background);
        background["ViewPort"] = View(0, 0, bar.Width, bar.Height);
        // La textura de la civ se apaga en sus nueve estados; la barra lisa es un hijo con material normal: un color
        // liso puesto en los estados por civ no se dibujó en el juego y se veía la textura (20261024-224921).
        var states = new JsonObject { ["UserStates"] = 9 };
        for (var i = 0; i < 9; i++)
            states[$"UserState{i}"] = None();
        background["StateMaterials"] = states;

        var entries = Detach(background);
        var widgets = entries.Select(e => e["Widget"]!.AsObject()).SelectMany(All).ToList();
        JsonObject Named(string name) =>
            widgets.FirstOrDefault(w => Text(w, "Name") == name) ?? throw new InvalidDataException($"No encontré {name} en el panel de recursos.");
        var fontIndex = Named("WoodStorage")["StateMaterials"]?["StateNormal"]?["Font"]?["FontIndex"]?.DeepClone() ?? JsonValue.Create(0);
        var white = new HudColor(1, 1, 1);

        // La barra cubre la pantalla entera menos el campo negro del reloj: está en otro panel con el mismo plano (1) y
        // no se sabe cuál se dibuja arriba, así que va en dos tramos, hasta el reloj y desde su final. El menú del juego
        // (escudo y botones, plano 11) queda arriba de la barra.
        var clockRect = layout.ClockRect();
        JsonObject BarPart(string name, int x, int width) => Wrap(new JsonObject
        {
            ["Type"] = "Base",
            ["Name"] = $"{Ours}{name}",
            ["ViewPort"] = View(x, 0, width, bar.Height),
            ["StateMaterials"] = new JsonObject { ["StateNormal"] = Flat(bar.Background) },
            ["ChildWidgets"] = new JsonArray(),
        });
        var afterClock = clockRect.X + clockRect.Width;
        // Con el log en el rincón (HudLayout.Log), la barra empieza a su derecha, en bar.X.
        var children = new JsonArray
        {
            BarPart("Bar", bar.X, Math.Min(bar.Width, clockRect.X) - bar.X),
            BarPart("BarAfterClock", afterClock, Math.Max(0, bar.Width - afterClock)),
            Border("Top", bar.X, 0, bar.Width - bar.X, bar.BorderWidth, bar),
            Border("Bottom", bar.X, bar.Height - bar.BorderWidth, bar.Width - bar.X, bar.BorderWidth, bar),
            Border("Left", bar.X, 0, bar.BorderWidth, bar.Height, bar),
            Border("Right", bar.Width - bar.BorderWidth, 0, bar.BorderWidth, bar.Height, bar),
        };
        var idle = layout.Cell("idle");
        var age = layout.Cell("age");
        var population = layout.Cell("population");
        foreach (var entry in entries)
        {
            var widget = entry["Widget"]!.AsObject();
            var name = Text(widget, "Name") ?? "";
            switch (name)
            {
                case "Wood" or "Food" or "Gold" or "Stone" or "Population":
                {
                    var cell = layout.Cell(name.ToLowerInvariant());
                    // Los fondos negros van como hermanos, antes del contenedor y de la etiqueta del stock y un plano
                    // más abajo: adentro del contenedor (plano 1 + 1) tapaban al stock (plano 2, hermano) en el juego.
                    for (var i = 0; i < cell.Backgrounds.Count; i++)
                    {
                        var b = cell.Backgrounds[i];
                        children.Add(Back($"{Ours}Back_{cell.Name}_{i}", cell.X + b.X, b.Y, b.Width, b.Height));
                    }
                    BuildContainer(widget, cell, bar.Height, name == "Population" ? "total" : "villagers", fontIndex, white);
                    break;
                }
                case "WoodStorage" or "FoodStorage" or "GoldStorage" or "StoneStorage":
                    PlaceLabel(widget, layout, layout.Cell(name[..^"Storage".Length].ToLowerInvariant()), "stock", fontIndex, white);
                    break;
                case "PopulationCount":
                    PlaceLabel(widget, layout, population, "cap", fontIndex, white);
                    break;
                case "PopulationFlash":
                    widget["ViewPort"] = Absolute(layout, population, population.Fields["cap"]);
                    break;
                case "Idle":
                    for (var i = 0; i < idle.Backgrounds.Count; i++)
                    {
                        var b = idle.Backgrounds[i];
                        children.Add(Back($"{Ours}Back_idle_{i}", idle.X + b.X, b.Y, b.Width, b.Height));
                    }
                    widget["ViewPort"] = View(idle.X + idle.Icon.X, idle.Icon.Y, idle.Icon.Size, idle.Icon.Size);
                    break;
                case "IdleWorkers":
                    PlaceLabel(widget, layout, idle, "count", fontIndex, white);
                    break;
                case "AgeUp":
                    widget["ViewPort"] = View(age.X + age.Icon.X, age.Icon.Y, age.Icon.Size, age.Icon.Size);
                    break;
                case "AgeBar":
                    var text = age.Fields["text"];
                    widget["ViewPort"] = Absolute(layout, age, text);
                    foreach (var label in Children(widget).Where(c => Text(c, "Name") == "AgeTextLabel"))
                    {
                        label["ViewPort"] = View(0, 0, text.Width, text.Height);
                        if (label["StateMaterials"]?["StateNormal"]?["Font"] is JsonObject font)
                            font["PointSize"] = text.Points;
                        label["TextAnchor"] = "CentreCentre";
                    }
                    break;
            }
            children.Add(entry);
        }
        background["ChildWidgets"] = children;
        return original;
    }

    /// <summary>El panel del reloj: el ancla corrida al final de la barra, el reloj grande y un rectángulo negro detrás.</summary>
    public static JsonObject ClockPanel(JsonObject original, HudLayout layout)
    {
        var spec = layout.Clock;
        var collection = original["Collection"]!.AsObject();
        // El origen de la colección se mide en unidades de pantalla (3840 de ancho): corrida a 2734, el reloj caía en
        // 1367 px (2734 × 0,5) con el HUD del usuario al 87 %, 178 px a la derecha de la barra (segunda prueba del
        // 2026-10-24). El ancla se mide en las unidades del HUD, como la barra (en el original, 1700 = 737 px a
        // 0,4348): el reloj se ubica con ella y la colección queda donde la trae el juego.
        if (collection["ViewPort"] is not JsonObject viewport)
            collection["ViewPort"] = viewport = View(0, 0, 3840, 2160);
        (viewport["xorigin"], viewport["yorigin"]) = (0, 0);
        var clock = collection["Widgets"]!.AsArray().Select(w => w!["Widget"]!.AsObject()).FirstOrDefault(w => Text(w, "Name") == "WorldTimer")
            ?? throw new InvalidDataException("No encontré el reloj en el panel.");
        StripOurs(clock);
        var anchorX = layout.ClockRect().X;
        if (clock["Anchor"] is JsonObject anchor)
            (anchor["xorigin"], anchor["yorigin"]) = (anchorX, 0);
        else
            clock["Anchor"] = new JsonObject { ["xorigin"] = anchorX, ["yorigin"] = 0, ["alignment"] = "TopLeft" };
        var label = Children(clock).FirstOrDefault(c => Text(c, "Name") == "WorldTimerLabel")
            ?? throw new InvalidDataException("No encontré el reloj en el panel.");
        if (label["StateMaterials"]?["StateNormal"]?["Font"] is JsonObject font)
            font["PointSize"] = spec.Points;
        label["ViewPort"] = View(ClockTextX, spec.Y + (spec.Height - spec.Points) / 2, 1000, spec.Points);
        label["ZPlaneLocalOffset"] = 1;
        // Espectadores e ID de partida van debajo del reloj: se bajan para que el reloj grande no los pise.
        foreach (var below in Children(clock).Where(c => Text(c, "Name") is "SpectatorsIcon" or "GameIDLabel"))
            below["ViewPort"]!["yorigin"] = spec.Y + spec.Height + 12;
        var back = Wrap(new JsonObject
        {
            ["Type"] = "Base",
            ["Name"] = $"{Ours}ClockBackground",
            ["ViewPort"] = View(0, spec.Y, spec.Width, spec.Height),
            ["StateMaterials"] = new JsonObject { ["StateNormal"] = Flat(new HudColor(0, 0, 0)) },
            ["ChildWidgets"] = new JsonArray(),
        });
        clock["ChildWidgets"] = new JsonArray([back, .. Detach(clock)]);
        return original;
    }

    /// <summary>
    /// El log de eventos según el diseño (<see cref="HudLayout.Log"/>), o null si el diseño no lo ubica: la colección
    /// corrida al lugar del diseño (su origen, en unidades de pantalla), el cuadro del log y su texto del tamaño del
    /// diseño (en unidades del HUD), un fondo del gris de la barra detrás (el del juego, <c>BlackPanel</c>, no se ve en
    /// la partida) y el chat a continuación.
    /// </summary>
    public static JsonObject? LogPanel(JsonObject original, HudLayout layout)
    {
        if (layout.Log is not { } log)
            return null;
        var k = layout.PixelsPerUnitAt1080p / PixelsPerScreenUnitAt1080p;
        int Su(int units) => (int)Math.Round(units * k);
        var collection = original["Collection"]!.AsObject();
        if (collection["ViewPort"] is not JsonObject viewport)
            collection["ViewPort"] = viewport = View(0, 0, 640, 1000);
        (viewport["xorigin"], viewport["yorigin"]) = (Su(log.X), Su(log.Y));
        var widgets = RemoveOurs(collection["Widgets"]!.AsArray());
        var events = widgets.Select(w => w!["Widget"]!.AsObject()).FirstOrDefault(w => Text(w, "Name") == "EventBackground")
            ?? throw new InvalidDataException("No encontré el log en el panel de avisos.");
        var (width, height) = (log.Width, log.Height);
        events["ViewPort"] = View(0, 0, width, height);
        if (events["Box"] is JsonObject box)
            (box["width"], box["height"]) = (width, height);
        if (Children(events).FirstOrDefault(c => Text(c, "Name") == "TextBox") is { } text)
        {
            text["ViewPort"] = View(LogTextMargin, LogTextMargin, width - 2 * LogTextMargin, height - 2 * LogTextMargin);
            if (text["TextBox"] is JsonObject inner)
                (inner["width"], inner["height"]) = (width - 2 * LogTextMargin, height - 2 * LogTextMargin);
        }
        if (widgets.Select(w => w!["Widget"]!.AsObject()).FirstOrDefault(w => Text(w, "Name") == "ChatBackground")?["ViewPort"] is JsonObject chat)
            chat["yorigin"] = height + 50;
        widgets.Insert(0, Wrap(new JsonObject
        {
            ["Type"] = "Base",
            ["Name"] = $"{Ours}LogBackground",
            ["ViewPort"] = View(0, 0, width, height),
            ["ZPlane"] = 1,
            ["StateMaterials"] = new JsonObject { ["StateNormal"] = Flat(layout.Bar.Background) },
            ["ChildWidgets"] = new JsonArray(),
        }));
        return original;
    }

    /// <summary>
    /// La cola de producción según el diseño (<see cref="HudLayout.Queue"/>), o null si el diseño no la ubica: el
    /// widget donde el juego pone los íconos se corre a la fila del diseño (en unidades del HUD, como la barra: en el
    /// original, y 115 = 50 px a 0,4348) y detrás va el gris de la barra con <see cref="HudLayout.QueueMargin"/> de margen.
    /// </summary>
    public static JsonObject? QueuePanel(JsonObject original, HudLayout layout)
    {
        if (layout.Queue is not { } queue)
            return null;
        var widgets = RemoveOurs(original["Collection"]!["Widgets"]!.AsArray());
        var icons = widgets.Select(w => w!["Widget"]!.AsObject()).FirstOrDefault(w => Text(w, "Name") == "Background")
            ?? throw new InvalidDataException("No encontré la cola en el panel de producción.");
        icons["ViewPort"] = View(queue.X, queue.Y, queue.Width, queue.Height);
        var m = HudLayout.QueueMargin;
        widgets.Insert(0, Wrap(new JsonObject
        {
            ["Type"] = "Base",
            ["Name"] = $"{Ours}QueueBackground",
            ["ViewPort"] = View(queue.X - m, queue.Y - m, queue.Width + 2 * m, queue.Height + 2 * m),
            ["ZPlane"] = 1,
            ["StateMaterials"] = new JsonObject { ["StateNormal"] = Flat(layout.Bar.Background) },
            ["ChildWidgets"] = new JsonArray(),
        }));
        return original;
    }

    /// <summary>Saca de la lista de widgets de una colección los que agregó el mod (por si el panel ya estaba armado).</summary>
    private static JsonArray RemoveOurs(JsonArray widgets)
    {
        for (var i = widgets.Count - 1; i >= 0; i--)
        {
            if ((Text(widgets[i]?["Widget"], "Name") ?? "").StartsWith(Ours, StringComparison.Ordinal))
                widgets.RemoveAt(i);
        }
        return widgets;
    }

    /// <summary>Cómo está el mod en cada perfil del juego (para la captura de diagnóstico), un renglón por perfil.</summary>
    public static IReadOnlyList<string> Describe(string? profilesRoot = null)
    {
        profilesRoot ??= DefaultProfilesRoot;
        if (!Directory.Exists(profilesRoot))
            return [$"No hay perfiles del juego en {profilesRoot}."];
        var lines = new List<string>();
        foreach (var profile in Directory.EnumerateDirectories(profilesRoot).Order())
        {
            var mods = Path.Combine(profile, "mods");
            if (!Directory.Exists(mods))
                continue;
            var name = Path.GetFileName(profile);
            if (!File.Exists(Path.Combine(mods, "local", Name, "widgetui", ResourcePanelFile)))
            {
                lines.Add($"Perfil {name}: sin instalar.");
                continue;
            }
            var status = ReadStatus(mods);
            var ours = status.FirstOrDefault(IsOurs);
            var ahead = status.Where(m => m.Enabled && !IsOurs(m) && (ours is null || m.Priority < ours.Priority) && TouchesPanels(mods, m))
                .Select(m => m.Title).ToList();
            lines.Add($"Perfil {name}: instalado, "
                + (ours is null ? "el juego todavía no lo listó" : ours.Enabled ? $"activo (prioridad {ours.Priority})" : "APAGADO")
                + (ahead.Count > 0 ? $"; le ganan: {string.Join(", ", ahead)}" : "") + ".");
        }
        return lines.Count > 0 ? lines : ["Ningún perfil del juego tiene carpeta de mods."];
    }

    /// <summary>
    /// Un contenedor de celda (Wood, Food, Gold, Stone, Population): transparente y del tamaño de la celda, con el
    /// ícono del juego, el casillero, los fondos, el separador y el número de aldeanos del juego como hijos.
    /// </summary>
    private static void BuildContainer(JsonObject widget, HudCell cell, int barHeight, string workersField, JsonNode fontIndex, HudColor white)
    {
        // El material del ícono del juego; en un panel ya armado el contenedor dice "None" y vale el nombre original.
        var iconMaterial = new[] { Text(widget["StateMaterials"]?["StateNormal"], "Material"), Text(widget["StateMaterials"]?["UserState0"], "Material") }
            .FirstOrDefault(m => m is { Length: > 0 } and not "None")
            ?? (cell.Name == "population" ? "Population" : $"Resource{Capital(cell.Name)}");
        // El envoltorio original del número de aldeanos ({ Widget: ... }): un nodo tiene un solo padre, así que se reutiliza.
        var workersEntry = ChildEntries(widget).FirstOrDefault(e => Text(e["Widget"], "Name") == "Workers")
            ?? throw new InvalidDataException($"No encontré los aldeanos de {Capital(cell.Name)} en el panel de recursos.");
        var workers = workersEntry["Widget"]!.AsObject();
        Detach(widget);
        widget["ViewPort"] = View(cell.X, 0, cell.Width, barHeight);
        widget["StateMaterials"] = cell.Name == "population"
            ? new JsonObject { ["UserStates"] = 2, ["UserState0"] = None(), ["UserState1"] = None() }
            : new JsonObject { ["StateNormal"] = None() };
        var children = new JsonArray
        {
            Wrap(new JsonObject
            {
                ["Type"] = "Base",
                ["Name"] = $"{Ours}Icon",
                ["ViewPort"] = View(cell.Icon.X, cell.Icon.Y, cell.Icon.Size, cell.Icon.Size),
                ["ZPlaneLocalOffset"] = 1,
                ["StateMaterials"] = new JsonObject { ["StateNormal"] = new JsonObject { ["Material"] = iconMaterial } },
                ["ChildWidgets"] = new JsonArray(),
            }),
        };
        if (cell.Chip is { } chip)
        {
            children.Add(Wrap(new JsonObject
            {
                ["Type"] = "Base",
                ["Name"] = $"{Ours}Chip",
                ["ViewPort"] = View(chip.X, chip.Y, chip.Width, chip.Height),
                ["ZPlaneLocalOffset"] = 1,
                ["StateMaterials"] = new JsonObject { ["StateNormal"] = Flat(chip.Color) },
                ["ChildWidgets"] = new JsonArray(),
            }));
        }
        if (cell.Separator is { } s)
        {
            children.Add(s.Text is { Length: > 0 } text
                ? Wrap(new JsonObject
                {
                    ["Type"] = "Label",
                    ["Name"] = $"{Ours}Separator",
                    ["ViewPort"] = View(s.X, s.Y, s.Width, s.Height),
                    ["StateMaterials"] = Font(fontIndex, s.Points, s.Color),
                    ["Text"] = text,
                    ["TextAnchor"] = "CentreCentre",
                    ["ZPlaneLocalOffset"] = 2,
                    ["ChildWidgets"] = new JsonArray(),
                })
                : Wrap(new JsonObject
                {
                    ["Type"] = "Base",
                    ["Name"] = $"{Ours}Separator",
                    ["ViewPort"] = View(s.X + (s.Width - 4) / 2, s.Y, 4, s.Height),
                    ["ZPlaneLocalOffset"] = 1,
                    ["StateMaterials"] = new JsonObject { ["StateNormal"] = Flat(s.Color) },
                    ["ChildWidgets"] = new JsonArray(),
                }));
        }
        var field = cell.Fields[workersField];
        workers["ViewPort"] = View(field.X, field.Y, field.Width, field.Height);
        workers["StateMaterials"] = Font(fontIndex, field.Points, white);
        workers["TextAnchor"] = "CentreCentre";
        workers["ZPlaneLocalOffset"] = 2;
        children.Add(workersEntry);
        widget["ChildWidgets"] = children;
    }

    /// <summary>Una etiqueta hermana del contenedor (stock, "N/M", inactivos) en coordenadas de la barra.</summary>
    private static void PlaceLabel(JsonObject label, HudLayout layout, HudCell cell, string fieldName, JsonNode fontIndex, HudColor white)
    {
        var field = cell.Fields[fieldName];
        label["ViewPort"] = Absolute(layout, cell, field);
        // El "N/M" del juego tiene tres estados (blanco, rojo al tope, amarillo): se conservan con el tamaño nuevo.
        if (label["StateMaterials"] is JsonObject states && states["UserStates"] is not null)
        {
            foreach (var (_, state) in states)
            {
                if (state is JsonObject { } s && s["Font"] is JsonObject font)
                    font["PointSize"] = field.Points;
            }
        }
        else
            label["StateMaterials"] = Font(fontIndex, field.Points, white);
        label["TextAnchor"] = "CentreCentre";
        label["ZPlaneLocalOffset"] = 2;
    }

    private static JsonObject Absolute(HudLayout layout, HudCell cell, HudField field) => View(cell.X + field.X, field.Y, field.Width, field.Height);

    private static JsonObject Border(string side, int x, int y, int w, int h, HudBar bar) => Wrap(new JsonObject
    {
        ["Type"] = "Base",
        ["Name"] = $"{Ours}Border{side}",
        ["ViewPort"] = View(x, y, w, h),
        ["ZPlaneLocalOffset"] = 1,
        ["StateMaterials"] = new JsonObject { ["StateNormal"] = Flat(bar.BorderColor) },
        ["ChildWidgets"] = new JsonArray(),
    });

    private static JsonObject Back(string name, int x, int y, int w, int h) => Wrap(new JsonObject
    {
        ["Type"] = "Base",
        ["Name"] = name,
        ["ViewPort"] = View(x, y, w, h),
        ["ZPlaneLocalOffset"] = 1,
        ["StateMaterials"] = new JsonObject { ["StateNormal"] = Flat(new HudColor(0, 0, 0)) },
        ["ChildWidgets"] = new JsonArray(),
    });

    /// <summary>Saca de un widget (y de sus hijos) todo lo que agregó el mod, por si el panel ya estaba armado.</summary>
    private static void StripOurs(JsonObject widget)
    {
        if (widget["ChildWidgets"] is not JsonArray children)
            return;
        for (var i = children.Count - 1; i >= 0; i--)
        {
            if (children[i] is not JsonObject entry || entry["Widget"] is not JsonObject child)
                continue;
            if ((Text(child, "Name") ?? "").StartsWith(Ours, StringComparison.Ordinal))
                children.RemoveAt(i);
            else
                StripOurs(child);
        }
    }

    private static string Capital(string name) => char.ToUpperInvariant(name[0]) + name[1..];

    private sealed record ModEntry(string Path, string Title, bool Enabled, int Priority);

    private static bool IsOurs(ModEntry mod) => mod.Path.Contains(Name, StringComparison.OrdinalIgnoreCase);

    /// <summary>La lista de mods del perfil (mod-status.json), o vacía si el juego todavía no la armó.</summary>
    private static List<ModEntry> ReadStatus(string mods)
    {
        var path = Path.Combine(mods, "mod-status.json");
        if (!File.Exists(path))
            return [];
        try
        {
            return (JsonNode.Parse(File.ReadAllText(path), documentOptions: ReadOptions)?["Mods"] as JsonArray ?? [])
                .OfType<JsonObject>()
                .Select(m => new ModEntry(
                    Text(m, "Path") ?? "",
                    Text(m, "Title") ?? Text(m, "Path") ?? "",
                    m["Enabled"]?.GetValueKind() == JsonValueKind.True,
                    m["Priority"] is JsonValue p && p.TryGetValue<int>(out var priority) ? priority : int.MaxValue))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string ModFolder(string mods, ModEntry mod) => Path.Combine(mods, mod.Path.Replace("//", "/").Replace('/', Path.DirectorySeparatorChar));

    private static bool TouchesPanels(string mods, ModEntry mod) =>
        new[] { ResourcePanelFile, ClockPanelFile, LogPanelFile, QueuePanelFile }.Any(f => File.Exists(Path.Combine(ModFolder(mods, mod), "widgetui", f)));

    private static JsonObject Read(string path) =>
        JsonNode.Parse(File.ReadAllText(path), documentOptions: ReadOptions)?.AsObject() ?? throw new InvalidDataException($"{path} está vacío.");

    private static void Write(string path, JsonNode node) =>
        File.WriteAllText(path, node.ToJsonString(WriteOptions), new UTF8Encoding(false));

    private static IEnumerable<JsonObject> ChildEntries(JsonObject widget) =>
        (widget["ChildWidgets"] as JsonArray ?? []).OfType<JsonObject>().Where(c => c["Widget"] is JsonObject);

    /// <summary>Los hijos de un widget, sacados de su lista para poder ponerlos en otra (un nodo tiene un solo padre).</summary>
    private static List<JsonObject> Detach(JsonObject widget)
    {
        var entries = ChildEntries(widget).ToList();
        (widget["ChildWidgets"] as JsonArray)?.Clear();
        return entries;
    }

    private static IEnumerable<JsonObject> Children(JsonObject widget) => ChildEntries(widget).Select(c => c["Widget"]!.AsObject());

    private static IEnumerable<JsonObject> All(JsonObject widget) => [widget, .. Children(widget).SelectMany(All)];

    private static string? Text(JsonNode? node, string name) => node?[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static JsonObject Color(HudColor color) => new() { ["r"] = Round(color.R), ["g"] = Round(color.G), ["b"] = Round(color.B), ["a"] = 1 };

    private static double Round(double value) => Math.Round(value, 3);

    private static JsonObject Flat(HudColor color) => new() { ["Material"] = "FlatColorWithAlpha", ["Color"] = Color(color) };

    private static JsonObject None() => new() { ["Material"] = "None" };

    private static JsonObject View(int x, int y, int width, int height) =>
        new() { ["xorigin"] = x, ["yorigin"] = y, ["width"] = width, ["height"] = height, ["alignment"] = "TopLeft" };

    private static JsonObject Wrap(JsonObject widget) => new() { ["Widget"] = widget };

    private static JsonObject Font(JsonNode fontIndex, int points, HudColor color) => new()
    {
        ["StateNormal"] = new JsonObject
        {
            ["Font"] = new JsonObject { ["FontIndex"] = fontIndex.DeepClone(), ["PointSize"] = points, ["Style"] = "Normal", ["TextColor"] = Color(color) },
        },
    };
}
