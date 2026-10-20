using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BoCoach.App.Capture;

/// <summary>Lo que dejó la instalación del mod: un renglón por perfil del juego y los avisos.</summary>
public sealed record HudModResult(IReadOnlyList<string> Installed, IReadOnlyList<string> Warnings)
{
    public bool Ok => Installed.Count > 0;
}

/// <summary>
/// El mod local "BoCoach HUD" (antes tools/mod/instalar-mod.ps1): parte de los paneles que el juego usa hoy (los del
/// mod activo con el número de prioridad más bajo, o los originales) y cambia dos cosas para leer mejor:
/// <list type="bullet">
/// <item>resourcepanel.json: el ícono de cada recurso y el de inactivos pasan a ser un cuadrado negro con una franja
/// arriba del color del recurso (azul en población, rojo en inactivos) y abajo la cantidad de aldeanos grande, en
/// blanco. Los stocks con la misma letra y tamaño, y detrás de cada stock y del "N/M" de población, un rectángulo
/// negro. La edad no cambia.</item>
/// <item>worldtimerpanel.json: el reloj más grande con un rectángulo negro liso detrás.</item>
/// </list>
/// Si el mod ya existe, parte de sus propios paneles: así no cambian las posiciones ni la calibración aunque se hayan
/// apagado los mods de los que salió. Los paneles se arman en la PC del jugador: no se redistribuye el JSON del juego
/// ni el de otros mods.
/// </summary>
public static class HudMod
{
    public const string Name = "BoCoach HUD";

    /// <summary>Donde el juego guarda un perfil por cuenta de Steam (cada uno con su carpeta mods).</summary>
    public static string DefaultProfilesRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games", "Age of Empires 2 DE");

    private const string ResourcePanelFile = "resourcepanel.json";
    private const string ClockPanelFile = "worldtimerpanel.json";

    private const int NumberPoints = 48;
    private const int ClockPoints = 56;
    // Rectángulo negro que tapa "00:00:00" (medido en una captura: a 40 pt el texto ocupa unas 121 unidades del panel).
    private const int ClockBackX = -10, ClockBackY = 4, ClockBackWidth = 200, ClockBackHeight = 74;

    private static readonly string[] Resources = ["Wood", "Food", "Gold", "Stone", "Population"];

    private static readonly Dictionary<string, (double R, double G, double B)> Colors = new()
    {
        ["Wood"] = (0.55, 0.35, 0.15),
        ["Food"] = (0.9, 0.4, 0.4),
        ["Gold"] = (0.95, 0.75, 0.1),
        ["Stone"] = (0.6, 0.6, 0.65),
        ["Population"] = (0.25, 0.5, 1),
        ["Idle"] = (0.9, 0, 0),
    };

    private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// Arma el mod en cada perfil del juego que tenga carpeta mods. <paramref name="game"/> es la carpeta del juego
    /// (de ahí salen los paneles originales); <paramref name="regenerate"/> vuelve a partir de los mods activos aunque
    /// el mod ya exista.
    /// </summary>
    public static HudModResult Install(string game, string? profilesRoot = null, bool regenerate = false)
    {
        var installed = new List<string>();
        var warnings = new List<string>();
        profilesRoot ??= DefaultProfilesRoot;
        if (!File.Exists(Path.Combine(game, "widgetui", ResourcePanelFile)))
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
            string Base(string file) =>
                !regenerate && File.Exists(Path.Combine(own, file)) ? Path.Combine(own, file) : ActivePanel(game, mods, active, file);
            var resourceBase = Base(ResourcePanelFile);
            var clockBase = Base(ClockPanelFile);

            var resources = ResourcePanel(Read(resourceBase));
            var clock = ClockPanel(Read(clockBase));
            Directory.CreateDirectory(own);
            Write(Path.Combine(own, ResourcePanelFile), resources);
            Write(Path.Combine(own, ClockPanelFile), clock);
            var info = new JsonObject
            {
                ["Author"] = "BoCoach",
                ["CacheStatus"] = 0,
                ["Description"] = "Aldeanos de cada recurso e inactivos en grande sobre fondo negro y fondo liso detrás del reloj, para que BoCoach los lea. "
                    + $"Basado en: {Origin(game, resourceBase)} (recursos), {Origin(game, clockBase)} (reloj)",
                ["Title"] = Name,
            };
            Write(Path.Combine(target, "info.json"), info);

            var name = Path.GetFileName(profile);
            installed.Add($"Perfil {name}: recursos de {Origin(game, resourceBase)}, reloj de {Origin(game, clockBase)}.");

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

    /// <summary>El panel de recursos con los cuadrados negros, las franjas de color y los números grandes.</summary>
    public static JsonObject ResourcePanel(JsonObject panel)
    {
        var widgets = panel["Collection"]!["Widgets"]!.AsArray().SelectMany(w => All(w!["Widget"]!.AsObject())).ToList();
        JsonObject Find(Func<JsonObject, bool> match, string what) =>
            widgets.FirstOrDefault(match) ?? throw new InvalidDataException($"No encontré {what} en el panel de recursos.");
        JsonObject Named(string name) => Find(w => Text(w, "Name") == name, name);

        var fontIndex = Named("WoodStorage")["StateMaterials"]?["StateNormal"]?["Font"]?["FontIndex"]?.DeepClone() ?? JsonValue.Create(0);
        // Rectángulo negro detrás de cada stock y del "N/M" de población, del alto de los cuadrados y separado de ellos
        // por un poco de panel: así el lector distingue el cuadrado del stock y nunca lee la textura del panel.
        var backs = new Dictionary<string, JsonObject>();
        foreach (var resource in Resources)
        {
            var icon = Find(w => Text(w, "Name") == resource && Text(w, "Type") == "Base", resource);
            var villagers = Children(icon).FirstOrDefault(c => Text(c, "Name") == "Workers")
                ?? throw new InvalidDataException($"No encontré los aldeanos de {resource} en el panel de recursos.");
            var (width, height) = (Number(icon["ViewPort"]!, "width"), Number(icon["ViewPort"]!, "height"));
            var band = Round(height * 0.43);
            var iconX = Number(icon["ViewPort"]!, "xorigin");
            JsonObject stock, next;
            if (resource == "Population")
            {
                // La población tiene dos estados (con ícono y sin él): los dos en negro.
                icon["StateMaterials"] = new JsonObject { ["UserStates"] = 2, ["UserState0"] = Flat(0, 0, 0), ["UserState1"] = Flat(0, 0, 0) };
                stock = Named("PopulationCount");
                next = Named("Idle");
            }
            else
            {
                icon["StateMaterials"] = new JsonObject { ["StateNormal"] = Flat(0, 0, 0) };
                stock = Named($"{resource}Storage");
                next = widgets.Where(w => Text(w, "Type") == "Base" && Resources.Contains(Text(w, "Name")) && Number(w["ViewPort"]!, "xorigin") > iconX)
                    .OrderBy(w => Number(w["ViewPort"]!, "xorigin")).First();
            }
            var limit = Number(next["ViewPort"]!, "xorigin") - Round(width / 2.0) - 4;
            // El fondo arranca un poco después del cuadrado y el texto un poco más adentro, para que no toque el borde.
            var x0 = iconX + Round(width / 2.0) + 5;
            var textX = Math.Max(Number(stock["ViewPort"]!, "xorigin"), x0 + 8);
            if (resource == "Population")
            {
                stock["ViewPort"]!["xorigin"] = textX;
            }
            else
            {
                stock["ViewPort"] = View(textX, Number(stock["ViewPort"]!, "yorigin"), limit - textX, NumberPoints, "CentreLeft");
                stock["StateMaterials"] = NumberFont(fontIndex);
            }
            stock["ZPlaneLocalOffset"] = 2;
            var stockName = Text(stock, "Name")!;
            backs[stockName] = Wrap(new JsonObject
            {
                ["Type"] = "Base",
                ["Name"] = $"BoCoachBack{stockName}",
                ["ViewPort"] = View(x0, Number(icon["ViewPort"]!, "yorigin") - Round(height / 2.0), limit - x0, height, "TopLeft"),
                ["ZPlaneLocalOffset"] = 1,
                ["StateMaterials"] = new JsonObject { ["StateNormal"] = Flat(0, 0, 0) },
                ["ChildWidgets"] = new JsonArray(),
            });
            villagers["ViewPort"] = View(0, band, width, height - band, "TopLeft");
            villagers["StateMaterials"] = NumberFont(fontIndex);
            villagers["TextAnchor"] = "CentreCentre";
            villagers["ZPlaneLocalOffset"] = 2;
            icon["ChildWidgets"] = new JsonArray([Band(width, band, Colors[resource]),
                .. Detach(icon).Where(c => Text(c["Widget"]!.AsObject(), "Name") != "BoCoachBand")]);
        }
        var background = panel["Collection"]!["Widgets"]![0]!["Widget"]!.AsObject();
        var children = new JsonArray();
        foreach (var child in Detach(background))
        {
            var name = Text(child["Widget"]!.AsObject(), "Name") ?? "";
            if (name.StartsWith("BoCoachBack", StringComparison.Ordinal))
                continue;
            if (backs.TryGetValue(name, out var back))
                children.Add(back);
            children.Add(child);
        }
        background["ChildWidgets"] = children;

        // Inactivos: el mismo cuadrado que los recursos, a su altura (el botón de KoBHV era alto para la flecha y sin
        // sus texturas el juego estira el ícono original).
        var wood = Find(w => Text(w, "Name") == "Wood" && Text(w, "Type") == "Base", "Wood")["ViewPort"]!;
        var button = Named("Idle");
        var idleNumber = Named("IdleWorkers");
        var (w, h) = (Number(wood, "width"), Number(wood, "height"));
        var idleBand = Round(h * 0.43);
        var (x, y) = (Number(button["ViewPort"]!, "xorigin"), Number(wood, "yorigin"));
        button["ViewPort"] = View(x, y, w, h, "CentreCentre");
        button["StateMaterials"] = new JsonObject
        {
            ["StateNormal"] = Flat(0, 0, 0),
            ["StateHover"] = Flat(0.25, 0.25, 0.25),
            ["StatePressed"] = Flat(0.25, 0.25, 0.25),
            ["StateDisabled"] = Flat(0, 0, 0),
        };
        button["ChildWidgets"] = new JsonArray(Band(w, idleBand, Colors["Idle"]));
        idleNumber["ViewPort"] = View(x - Round(w / 2.0), y - Round(h / 2.0) + idleBand, w, h - idleBand, "TopLeft");
        idleNumber["StateMaterials"] = NumberFont(fontIndex);
        idleNumber["TextAnchor"] = "CentreCentre";
        return panel;
    }

    /// <summary>El panel del reloj con la letra más grande y un rectángulo negro detrás.</summary>
    public static JsonObject ClockPanel(JsonObject panel)
    {
        var clock = panel["Collection"]!["Widgets"]!.AsArray().Select(w => w!["Widget"]!.AsObject()).FirstOrDefault(w => Text(w, "Name") == "WorldTimer")
            ?? throw new InvalidDataException("No encontré el reloj en el panel.");
        var label = Children(clock).FirstOrDefault(c => Text(c, "Name") == "WorldTimerLabel")
            ?? throw new InvalidDataException("No encontré el reloj en el panel.");
        label["StateMaterials"]!["StateNormal"]!["Font"]!["PointSize"] = ClockPoints;
        label["ViewPort"]!["height"] = ClockPoints;
        // Espectadores e ID de partida van debajo del reloj: se bajan para que el reloj grande no los pise.
        foreach (var below in Children(clock).Where(c => Text(c, "Name") is "SpectatorsIcon" or "GameIDLabel"))
            below["ViewPort"]!["yorigin"] = Number(label["ViewPort"]!, "yorigin") + ClockPoints + 12;
        label["ZPlaneLocalOffset"] = 1;
        var back = Wrap(new JsonObject
        {
            ["Type"] = "Base",
            ["Name"] = "BoCoachClockBackground",
            ["ViewPort"] = View(ClockBackX, ClockBackY, ClockBackWidth, ClockBackHeight, "TopLeft"),
            ["StateMaterials"] = new JsonObject { ["StateNormal"] = Flat(0, 0, 0) },
            ["ChildWidgets"] = new JsonArray(),
        });
        clock["ChildWidgets"] = new JsonArray([back, .. Detach(clock).Where(c => Text(c["Widget"]!.AsObject(), "Name") != "BoCoachClockBackground")]);
        return panel;
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

    /// <summary>El panel que el juego usa hoy: el del primer mod activo que lo tenga, o el original.</summary>
    private static string ActivePanel(string game, string mods, IEnumerable<ModEntry> active, string file) =>
        active.Select(m => Path.Combine(ModFolder(mods, m), "widgetui", file)).FirstOrDefault(File.Exists)
        ?? Path.Combine(game, "widgetui", file);

    /// <summary>De dónde salió un panel: "original" o el nombre de la carpeta del mod.</summary>
    private static string Origin(string game, string panel) =>
        panel.StartsWith(game, StringComparison.OrdinalIgnoreCase) ? "original" : Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(panel))) ?? panel;

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

    private static string? Text(JsonObject node, string name) => node[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static int Number(JsonNode node, string name) => node[name] switch
    {
        JsonValue v when v.TryGetValue<int>(out var i) => i,
        JsonValue v when v.TryGetValue<double>(out var d) => (int)Math.Round(d),
        _ => 0,
    };

    // Como el [int] de PowerShell del script original: redondea al par (42,5 → 42).
    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.ToEven);

    private static JsonObject Color(double r, double g, double b) => new() { ["r"] = r, ["g"] = g, ["b"] = b, ["a"] = 1 };

    private static JsonObject Flat(double r, double g, double b) => new() { ["Material"] = "FlatColorWithAlpha", ["Color"] = Color(r, g, b) };

    private static JsonObject View(int x, int y, int width, int height, string alignment) =>
        new() { ["xorigin"] = x, ["yorigin"] = y, ["width"] = width, ["height"] = height, ["alignment"] = alignment };

    private static JsonObject Wrap(JsonObject widget) => new() { ["Widget"] = widget };

    private static JsonObject NumberFont(JsonNode fontIndex) => new()
    {
        ["StateNormal"] = new JsonObject
        {
            ["Font"] = new JsonObject { ["FontIndex"] = fontIndex.DeepClone(), ["PointSize"] = NumberPoints, ["Style"] = "Normal", ["TextColor"] = Color(1, 1, 1) },
        },
    };

    private static JsonObject Band(int width, int height, (double R, double G, double B) color) => Wrap(new JsonObject
    {
        ["Type"] = "Base",
        ["Name"] = "BoCoachBand",
        ["ViewPort"] = View(0, 0, width, height, "TopLeft"),
        ["ZPlaneLocalOffset"] = 1,
        ["StateMaterials"] = new JsonObject { ["StateNormal"] = Flat(color.R, color.G, color.B) },
        ["ChildWidgets"] = new JsonArray(),
    });
}
