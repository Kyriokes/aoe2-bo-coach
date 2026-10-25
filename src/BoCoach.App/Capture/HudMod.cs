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
                    warnings.Add($"Perfil {name}: \"{mod.Title}\" también cambia el panel de recursos o el reloj"
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

        var children = new JsonArray
        {
            Wrap(new JsonObject
            {
                ["Type"] = "Base",
                ["Name"] = $"{Ours}Bar",
                ["ViewPort"] = View(0, 0, bar.Width, bar.Height),
                ["StateMaterials"] = new JsonObject { ["StateNormal"] = Flat(bar.Background) },
                ["ChildWidgets"] = new JsonArray(),
            }),
            Border("Top", 0, 0, bar.Width, bar.BorderWidth, bar),
            Border("Bottom", 0, bar.Height - bar.BorderWidth, bar.Width, bar.BorderWidth, bar),
            Border("Left", 0, 0, bar.BorderWidth, bar.Height, bar),
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

    /// <summary>El panel del reloj: la colección corrida al final de la barra, el reloj grande y un rectángulo negro detrás.</summary>
    public static JsonObject ClockPanel(JsonObject original, HudLayout layout)
    {
        var spec = layout.Clock;
        var collection = original["Collection"]!.AsObject();
        if (collection["ViewPort"] is not JsonObject viewport)
            collection["ViewPort"] = viewport = View(0, 0, 3840, 2160);
        viewport["xorigin"] = layout.Bar.Width + spec.Gap;
        viewport["yorigin"] = 0;
        var clock = collection["Widgets"]!.AsArray().Select(w => w!["Widget"]!.AsObject()).FirstOrDefault(w => Text(w, "Name") == "WorldTimer")
            ?? throw new InvalidDataException("No encontré el reloj en el panel.");
        StripOurs(clock);
        // El ancla del original (1700) se suma a la colección: con las dos el reloj caía fuera de la pantalla
        // (20261024-224921). XavilUI, que también corre la colección, la deja en 0.
        if (clock["Anchor"] is JsonObject anchor)
            (anchor["xorigin"], anchor["yorigin"]) = (0, 0);
        else
            clock["Anchor"] = new JsonObject { ["xorigin"] = 0, ["yorigin"] = 0, ["alignment"] = "TopLeft" };
        var label = Children(clock).FirstOrDefault(c => Text(c, "Name") == "WorldTimerLabel")
            ?? throw new InvalidDataException("No encontré el reloj en el panel.");
        if (label["StateMaterials"]?["StateNormal"]?["Font"] is JsonObject font)
            font["PointSize"] = spec.Points;
        label["ViewPort"] = View(8, spec.Y + (spec.Height - spec.Points) / 2, 1000, spec.Points);
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
        File.Exists(Path.Combine(ModFolder(mods, mod), "widgetui", ResourcePanelFile)) || File.Exists(Path.Combine(ModFolder(mods, mod), "widgetui", ClockPanelFile));

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
