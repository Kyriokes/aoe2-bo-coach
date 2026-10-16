// ReplaySession: corre la visión sobre una grabación (sin el juego) y arma las letras del HUD.
//
//   glyphs <grabación> <región[,región...]> <tinta> <salida> [--every N] [--ratio-percent 70]
//       Recorta los caracteres de la región, los agrupa por parecido y escribe clusters.json y
//       clusters.png (una fila por grupo, en el mismo orden) para etiquetarlos mirando la imagen.
//   font <salida> <etiquetas> <nombre> <tinta> <fuente.json>
//       Arma la letra con las etiquetas "grupo=carácter" separadas por coma (por ejemplo "0=0,1=:,2=3").
//       Los grupos sin etiqueta se descartan.
//   read <grabación> <región> <fuente.json> [--frames 1,2,3 | --every N]
//       Lee la región con la letra y muestra el texto y la seguridad de cada captura.
//   ocr <grabación> <región> [--frames 1,2,3 | --every N]
//       Pasa el OCR de Windows (el que se usa para el log) y muestra las capturas donde leyó algo,
//       con cada renglón como "altura:texto", separados por '|'.
//
//   replay <grabación> [--fonts data/fonts] [--events data/events.json] [--no-log] [--out lecturas.csv]
//       Corre el pipeline completo (números, suavizado, edad y log) y escribe una fila por captura.
//
// <tinta> es "white" (números blancos) o "blue" (números azules).

using System.Globalization;
using System.Text.Json;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Digits;
using BoCoach.Vision.Recording;
using BoCoach.Core.Data;
using BoCoach.Vision.Events;
using BoCoach.Vision.Hud;
using BoCoach.Vision.Regions;

if (args.Length == 0)
{
    Console.WriteLine("Uso: ReplaySession glyphs|font|read ... (ver el comentario al principio de Program.cs)");
    return 1;
}

try
{
    return args[0] switch
    {
        "glyphs" => Glyphs(args[1], args[2], Ink(args[3]), args[4], Option(args, "--every", 1), Option(args, "--ratio-percent", 70) / 100.0),
        "font" => Font(args[1], args[2], args[3], Ink(args[4]), args[5]),
        "read" => Read(args[1], args[2], args[3], args),
        "ocr" => Ocr(args[1], args[2], args).GetAwaiter().GetResult(),
        "replay" => Replay(args[1], args).GetAwaiter().GetResult(),
        _ => throw new ArgumentException($"Comando desconocido: {args[0]}"),
    };
}
catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException or IOException or InvalidDataException)
{
    Console.Error.WriteLine($"Error: {e.Message}");
    return 1;
}

static InkSpec Ink(string color) => color switch
{
    "white" => new InkSpec { Color = "white", Low = 120, High = 200 },
    "blue" => new InkSpec { Color = "blue", Low = 120, High = 190 },
    _ => throw new ArgumentException($"Tinta desconocida: {color} (usá white o blue)."),
};

static int Option(string[] args, string name, int fallback)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? int.Parse(args[i + 1], CultureInfo.InvariantCulture) : fallback;
}

static IEnumerable<int> SelectFrames(RecordedSession session, string[] args)
{
    var i = Array.IndexOf(args, "--frames");
    if (i >= 0 && i + 1 < args.Length)
        return args[i + 1].Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture));
    var every = Option(args, "--every", 1);
    return session.Frames.Select(f => f.Index).Where(f => f % every == 0);
}

// ---- glyphs: recortar y agrupar ----
static int Glyphs(string recording, string region, InkSpec ink, string output, int every, double ratio)
{
    var session = RecordedSession.Load(recording);
    var samples = new List<float[]>();
    var ratios = new List<float>();
    foreach (var frame in session.Frames.Where(f => f.Index % every == 0))
    {
        // Ancho típico provisorio de un dígito (--ratio-percent, 70 = 0,7 del alto): sirve para partir dígitos pegados.
        foreach (var g in region.Split(',').SelectMany(r => BoCoach.Vision.Digits.Glyphs.Extract(session.Image(frame.Index, r), ink, ratio)))
        {
            samples.Add(g.Cell);
            ratios.Add(g.WidthRatio);
        }
    }

    // Agrupamiento simple: cada muestra va al grupo más parecido, o abre uno nuevo.
    const float joinDistance = 6f;
    var clusters = new List<(float[] Sum, int Count, List<int> Members, List<float> Ratios)>();
    for (var i = 0; i < samples.Count; i++)
    {
        var best = -1;
        var bestDistance = float.MaxValue;
        for (var c = 0; c < clusters.Count; c++)
        {
            var centroid = clusters[c].Sum.Select(v => v / clusters[c].Count).ToArray();
            var d = BoCoach.Vision.Digits.Glyphs.Distance(samples[i], centroid);
            if (d < bestDistance)
                (best, bestDistance) = (c, d);
        }
        if (best >= 0 && bestDistance < joinDistance)
        {
            var c = clusters[best];
            for (var k = 0; k < c.Sum.Length; k++)
                c.Sum[k] += samples[i][k];
            c.Members.Add(i);
            c.Ratios.Add(ratios[i]);
            clusters[best] = (c.Sum, c.Count + 1, c.Members, c.Ratios);
        }
        else
        {
            clusters.Add(((float[])samples[i].Clone(), 1, [i], [ratios[i]]));
        }
    }

    var ordered = clusters.OrderByDescending(c => c.Count).ToList();
    Directory.CreateDirectory(output);
    var json = ordered.Select((c, id) => new
    {
        id,
        count = c.Count,
        width_ratio = c.Ratios.Order().ElementAt(c.Ratios.Count / 2),
        centroid = c.Sum.Select(v => v / c.Count).ToArray(),
    });
    File.WriteAllText(Path.Combine(output, "clusters.json"), JsonSerializer.Serialize(json));

    // Imagen: una fila por grupo con hasta 16 ejemplos (×4), en el mismo orden que clusters.json.
    const int zoom = 4, perRow = 16, gap = 4;
    var cellW = GlyphFont.CellWidth * zoom;
    var cellH = GlyphFont.CellHeight * zoom;
    var width = perRow * (cellW + gap) + gap;
    var height = ordered.Count * (cellH + gap) + gap;
    var pixels = new byte[width * height * 4];
    for (var p = 0; p < pixels.Length; p += 4)
        (pixels[p], pixels[p + 1], pixels[p + 2], pixels[p + 3]) = (70, 50, 40, 255);
    for (var row = 0; row < ordered.Count; row++)
    {
        var members = ordered[row].Members;
        for (var k = 0; k < Math.Min(perRow, members.Count); k++)
        {
            var cell = samples[members[k * members.Count / Math.Min(perRow, members.Count)]];
            var ox = gap + k * (cellW + gap);
            var oy = gap + row * (cellH + gap);
            for (var y = 0; y < cellH; y++)
            {
                for (var x = 0; x < cellW; x++)
                {
                    var v = (byte)(30 + 225 * cell[y / zoom * GlyphFont.CellWidth + x / zoom]);
                    var o = ((oy + y) * width + ox + x) * 4;
                    (pixels[o], pixels[o + 1], pixels[o + 2]) = (v, v, v);
                }
            }
        }
    }
    Png.Write(new CapturedImage(width, height, pixels), Path.Combine(output, "clusters.png"));

    Console.WriteLine($"{samples.Count} caracteres en {ordered.Count} grupos (fila = grupo):");
    for (var id = 0; id < ordered.Count; id++)
        Console.WriteLine($"  grupo {id,2}: {ordered[id].Count,5} muestras, ancho/alto {ordered[id].Ratios.Order().ElementAt(ordered[id].Ratios.Count / 2):0.00}");
    return 0;
}

// ---- font: armar la letra con las etiquetas ----
static int Font(string output, string labels, string name, InkSpec ink, string fontPath)
{
    using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "clusters.json")));
    var clusters = doc.RootElement.EnumerateArray().ToDictionary(
        c => c.GetProperty("id").GetInt32(),
        c => (Count: c.GetProperty("count").GetInt32(),
              Ratio: c.GetProperty("width_ratio").GetSingle(),
              Centroid: c.GetProperty("centroid").EnumerateArray().Select(v => v.GetSingle()).ToArray()));

    // Una plantilla por grupo etiquetado (las variantes de un mismo carácter no se promedian).
    var templates = new List<GlyphTemplate>();
    var digitRatios = new List<float>();
    foreach (var pair in labels.Split(',', StringSplitOptions.RemoveEmptyEntries))
    {
        var parts = pair.Split('=');
        var id = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var ch = parts[1].Length == 1 ? parts[1][0] : throw new ArgumentException($"Etiqueta inválida: {pair}");
        var c = clusters[id];
        templates.Add(new GlyphTemplate(ch, c.Centroid));
        if (char.IsDigit(ch) && ch != '1')
            digitRatios.AddRange(Enumerable.Repeat(c.Ratio, c.Count));
    }

    digitRatios.Sort();
    var typical = digitRatios.Count == 0 ? 0.55 : digitRatios[digitRatios.Count / 2];
    var font = new GlyphFont(name, ink, templates, typical);
    font.Save(fontPath);
    Console.WriteLine($"Letra '{name}': {templates.Count} plantillas de {font.Chars.Count()} caracteres ({string.Concat(font.Chars)}), ancho típico {typical:0.00}: {fontPath}");
    return 0;
}

// ---- read: leer una región con una letra ----
static int Read(string recording, string region, string fontPath, string[] args)
{
    var session = RecordedSession.Load(recording);
    var reader = new GlyphReader(GlyphFont.Load(fontPath));
    foreach (var frame in SelectFrames(session, args))
    {
        var reading = reader.Read(session.Image(frame, region));
        Console.WriteLine($"{frame,6}  {reading.Text,-12} {reading.Confidence:0.00}");
    }
    return 0;
}

// ---- ocr: OCR de Windows sobre una región (para el log de eventos) ----
static async Task<int> Ocr(string recording, string region, string[] args)
{
    var session = RecordedSession.Load(recording);
    var ocr = BoCoach.Vision.Ocr.OcrReader.TryCreate() ?? throw new InvalidDataException("El OCR en inglés de Windows no está instalado.");
    var withText = 0;
    foreach (var frame in SelectFrames(session, args))
    {
        var lines = await ocr.ReadLinesAsync(session.Image(frame, region));
        var text = string.Join("|", lines.Select(l => $"{l.Top:0}:{l.Text.Replace("|", "/").Trim()}"));
        if (text.Length == 0)
            continue;
        withText++;
        Console.WriteLine($"{frame,6}  {text}");
    }
    Console.WriteLine($"{withText} capturas con texto");
    return 0;
}

// ---- replay: pipeline completo sobre una grabación ----
static async Task<int> Replay(string recording, string[] args)
{
    string Text(string name, string fallback)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }

    var session = RecordedSession.Load(recording);
    var reader = new HudReader(HudFonts.Load(Text("--fonts", Path.Combine("data", "fonts"))));
    var tracker = new HudTracker();
    var ocr = BoCoach.Vision.Ocr.OcrReader.TryCreate();
    var useLog = !args.Contains("--no-log") && ocr is not null && session.Regions.Contains(RegionNames.EventLog);
    var log = useLog ? new EventLogReader(ocr!, EventCatalog.Load(Text("--events", Path.Combine("data", "events.json")))) : null;
    var output = Text("--out", Path.Combine(recording, "lecturas.csv"));

    var raw = new Dictionary<string, int>();
    var clipped = new Dictionary<string, int>();
    var events = 0;
    await using var csv = new StreamWriter(output);
    await csv.WriteLineAsync("frame,time_ms,clock,food,wood,gold,stone,villagers,idle,age,events");
    foreach (var frame in session.Frames)
    {
        var reading = reader.Read(session, frame.Index);
        foreach (var (name, value) in new (string, object?)[] { ("clock", reading.Clock), ("food", reading.Food), ("wood", reading.Wood), ("gold", reading.Gold), ("stone", reading.Stone), ("villagers", reading.Villagers) })
            if (value is not null)
                raw[name] = raw.GetValueOrDefault(name) + 1;
        foreach (var region in reading.Clipped)
            clipped[region] = clipped.GetValueOrDefault(region) + 1;

        IReadOnlyList<string> newEvents = log is null ? [] : await log.ReadAsync(session.Image(frame.Index, RegionNames.EventLog));
        events += newEvents.Count;
        BoCoach.Core.Data.Age? age = ocr is not null && session.Has(frame.Index, RegionNames.Age)
            ? AgeText.Parse(await ocr.ReadAsync(session.Image(frame.Index, RegionNames.Age)))
            : null;

        var state = tracker.Update(frame.TimeMs / 1000.0, reading, newEvents, age);
        await csv.WriteLineAsync(string.Join(",",
            frame.Index, frame.TimeMs,
            state.Clock is { } c ? BoCoach.Core.GameClock.Format(c) : "",
            state.Food, state.Wood, state.Gold, state.Stone, state.Villagers, state.IdleVillagers, state.Age,
            string.Join(" ", state.NewEvents)));
    }

    var total = session.Frames.Count;
    Console.WriteLine($"{total} capturas → {output}");
    foreach (var name in new[] { "clock", "food", "wood", "gold", "stone", "villagers" })
        Console.WriteLine($"  {name,-10} lecturas válidas: {raw.GetValueOrDefault(name) * 100.0 / total,5:0.0} %");
    foreach (var (region, n) in clipped.OrderByDescending(c => c.Value))
        Console.WriteLine($"  aviso: en {region} el número toca el borde de la caja en {n} capturas: agrandá esa caja al calibrar.");
    Console.WriteLine(log is null ? "  log: no se leyó" : $"  log: {events} eventos");
    return 0;
}
