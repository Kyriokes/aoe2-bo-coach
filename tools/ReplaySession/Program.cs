// ReplaySession: corre la visión sobre una grabación (sin el juego) y arma las letras del HUD.
//
//   glyphs <grabación[;grabación...]> <región[,región...]> <tinta> <salida> [--every N] [--ratio-percent 70]
//       Recorta los caracteres de la región, los agrupa por parecido y escribe clusters.json y
//       clusters.png (una fila por grupo, en el mismo orden) para etiquetarlos mirando la imagen.
//   font <salida> <etiquetas> <nombre> <tinta> <fuente.json>
//       Arma la letra con las etiquetas "grupo=carácter" separadas por coma (por ejemplo "0=0,1=:,2=3").
//       Los grupos sin etiqueta se descartan.
//   read <grabación> <región> <fuente.json> [--frames 1,2,3 | --every N]
//       Lee la región con la letra y muestra el texto y la seguridad de cada captura.
//   ocr <grabación> <región> [--frames 1,2,3 | --every N]
//       Pasa el OCR de Windows y muestra las capturas donde leyó algo, con cada renglón como "altura:texto",
//       separados por '|'. Para event_log prepara la captura igual que el lector del log (LogImage).
//
//   replay <grabación> [--fonts data/fonts] [--events data/events.json] [--no-log] [--out lecturas.csv]
//       Corre la visión completa (números, suavizado, edad y log) y escribe una fila por captura.
//   coach <grabación | lecturas.csv> <bo.json> [--fonts ...] [--events ...] [--game-data data/game_data]
//         [--economy] [--allocator exact|greedy]
//       Corre el loop completo (visión, motor y economía) y muestra lo que diría el overlay cada vez que cambia,
//       y al final el estado de cada paso.
//   bench <grabación> [--frames 300]
//       Mide cuánto tarda cada parte de la lectura (HUD, log, edad) para estimar la CPU en vivo.
//
// <tinta> es "white" (números blancos), "white-auto" (blancos con umbral por imagen), "white-on-black" (blancos sobre
// el negro del mod) o "blue" (números azules).
// Con el HUD del mod BoCoach HUD, <región> puede ser "wood:square" (el número del cuadrado negro) o "wood:stock"
// (lo que está a la derecha del cuadrado).

using System.Globalization;
using System.Text.Json;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Digits;
using BoCoach.Vision.Recording;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;
using BoCoach.Vision.Events;
using BoCoach.Vision.Hud;
using BoCoach.Vision.Regions;

// UTF-8 para que los acentos (y lo que lea el OCR) se vean bien y se puedan guardar en archivos de prueba.
Console.OutputEncoding = System.Text.Encoding.UTF8;

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
        "coach" => CoachReplay(args[1], args[2], args).GetAwaiter().GetResult(),
        "bench" => Bench(args[1], args).GetAwaiter().GetResult(),
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
    // Blanco puro: el fondo de algunos diseños de HUD (por civ) es gris claro y no tiene que contar como tinta.
    "white" => new InkSpec { Color = "white", Low = 180, High = 235 },
    // Umbral por imagen (Otsu) entre 110 y 200: para los números del stock en los distintos diseños de HUD.
    "white-auto" => new InkSpec { Color = "white", Low = 110, High = 200, Adaptive = true },
    "blue" => new InkSpec { Color = "blue", Low = 120, High = 190 },
    // Blanco sobre el negro puro del mod BoCoach HUD: umbral bajo para que entren los trazos finos (el "0" del reloj
    // tiene las curvas de arriba y abajo muy finas y con 180 se partía en dos palitos).
    "white-on-black" => new InkSpec { Color = "white", Low = 50, High = 150 },
    _ => throw new ArgumentException($"Tinta desconocida: {color} (usá white, white-auto, white-on-black o blue)."),
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
    // Varias grabaciones separadas por ';' (por ejemplo, partidas con distintos diseños de HUD).
    var samples = new List<float[]>();
    var ratios = new List<float>();
    foreach (var session in recording.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(RecordedSession.Load))
    {
        foreach (var frame in session.Frames.Where(f => f.Index % every == 0))
        {
            // Ancho típico provisorio de un dígito (--ratio-percent, 70 = 0,7 del alto): sirve para partir dígitos pegados.
            foreach (var g in region.Split(',').Select(r => Part(session, frame.Index, r)).OfType<CapturedImage>()
                         .SelectMany(image => BoCoach.Vision.Digits.Glyphs.Extract(image, ink, ratio)))
            {
                samples.Add(g.Cell);
                ratios.Add(g.WidthRatio);
            }
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

    // Imágenes: una fila por grupo con hasta 16 ejemplos (×4), en el mismo orden que clusters.json, en páginas
    // de 20 grupos (clusters.png = grupos 0-19, clusters-2.png = 20-39, ...) para que se vean grandes.
    const int zoom = 4, perRow = 16, gap = 4, rowsPerPage = 20;
    var cellW = GlyphFont.CellWidth * zoom;
    var cellH = GlyphFont.CellHeight * zoom;
    var width = perRow * (cellW + gap) + gap;
    for (var page = 0; page * rowsPerPage < ordered.Count; page++)
    {
        var rows = ordered.Skip(page * rowsPerPage).Take(rowsPerPage).ToList();
        var height = rows.Count * (cellH + gap) + gap;
        var pixels = new byte[width * height * 4];
        for (var p = 0; p < pixels.Length; p += 4)
            (pixels[p], pixels[p + 1], pixels[p + 2], pixels[p + 3]) = (70, 50, 40, 255);
        for (var row = 0; row < rows.Count; row++)
        {
            var members = rows[row].Members;
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
        var name = page == 0 ? "clusters.png" : $"clusters-{page + 1}.png";
        Png.Write(new CapturedImage(width, height, pixels), Path.Combine(output, name));
    }

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
        if (Part(session, frame, region) is not { } image)
        {
            Console.WriteLine($"{frame,6}  (sin el cuadrado del mod)");
            continue;
        }
        var reading = region.EndsWith(":stock", StringComparison.Ordinal) ? reader.ReadRightmostGroup(image) : reader.Read(image);
        Console.WriteLine($"{frame,6}  {reading.Text,-12} {reading.Confidence:0.00}");
    }
    return 0;
}

// "región", o con el HUD del mod "región:square" (el número del cuadrado) y "región:stock" (lo que está a su derecha).
static CapturedImage? Part(RecordedSession session, int frame, string spec)
{
    var parts = spec.Split(':');
    var image = session.Image(frame, parts[0]);
    if (parts.Length == 1)
        return image;
    return ModPanel.Split(image) is { } split
        ? parts[1] switch
        {
            "square" => split.Square,
            "stock" => split.Stock,
            _ => throw new ArgumentException($"Parte desconocida: {parts[1]} (square o stock)"),
        }
        : null;
}

// ---- ocr: OCR de Windows sobre una región (para el log de eventos) ----
static async Task<int> Ocr(string recording, string region, string[] args)
{
    var session = RecordedSession.Load(recording);
    var ocr = BoCoach.Vision.Ocr.OcrReader.TryCreate() ?? throw new InvalidDataException("El OCR en inglés de Windows no está instalado.");
    var withText = 0;
    foreach (var frame in SelectFrames(session, args))
    {
        var image = session.Image(frame, region);
        var lines = region == RegionNames.EventLog ? await EventLogReader.ReadLinesAsync(ocr, image) : await ocr.ReadLinesAsync(image);
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
    var session = RecordedSession.Load(recording);
    var reader = Reader(session, args);
    var output = Text(args, "--out", Path.Combine(recording, "lecturas.csv"));

    var valid = new Dictionary<string, int>();
    var clipped = new Dictionary<string, int>();
    var events = 0;
    await using var csv = new StreamWriter(output);
    await csv.WriteLineAsync(ScreenReadingLog.Header);
    foreach (var frame in session.Frames)
    {
        var state = await reader.ReadAsync(Regions(session, frame.Index), frame.TimeMs / 1000.0);
        foreach (var (name, value) in new (string, object?)[] { ("clock", state.Clock), ("food", state.Food), ("wood", state.Wood), ("gold", state.Gold), ("stone", state.Stone), ("villagers", state.Villagers) })
            if (value is not null)
                valid[name] = valid.GetValueOrDefault(name) + 1;
        foreach (var region in state.Clipped)
            clipped[region] = clipped.GetValueOrDefault(region) + 1;
        events += state.NewEvents.Count;

        await csv.WriteLineAsync(ScreenReadingLog.Format(frame.Index, frame.TimeMs, state.ToReading(), state.IdleVillagers, state.Age?.ToString()));
    }

    var total = session.Frames.Count;
    Console.WriteLine($"{total} capturas → {output}");
    foreach (var name in new[] { "clock", "food", "wood", "gold", "stone", "villagers" })
        Console.WriteLine($"  {name,-10} lecturas suavizadas: {valid.GetValueOrDefault(name) * 100.0 / total,5:0.0} %");
    foreach (var (region, n) in clipped.OrderByDescending(c => c.Value))
        Console.WriteLine($"  aviso: en {region} el número toca el borde de la caja en {n} capturas: agrandá esa caja al calibrar.");
    Console.WriteLine(args.Contains("--no-log") ? "  log: no se leyó" : $"  log: {events} eventos");
    return 0;
}

// ---- coach: el loop completo (visión + motor + economía) sobre una grabación o sus lecturas.csv ----
static async Task<int> CoachReplay(string recording, string boPath, string[] args)
{
    var data = GameDataLoader.LoadLatest(Text(args, "--game-data", Path.Combine("data", "game_data")));
    var catalog = Catalog(args, data);
    var bo = BuildOrderSerializer.Load(boPath);
    var builder = new SnapshotBuilder(data);
    var allocator = Enum.Parse<BoCoach.Core.Economy.AllocatorKind>(Text(args, "--allocator", "exact"), ignoreCase: true);
    var coach = new Coach(bo, data, catalog, economy: new BoCoach.Core.Economy.EconomySettings { Allocator = allocator });

    string? last = null;
    BoCoach.Core.Economy.Suggestion? shown = null;
    var suggestions = 0;
    await foreach (var (index, reading) in Readings(recording, args))
    {
        if (builder.Build(reading) is not { } snapshot)
            continue;
        var view = coach.Update(snapshot);
        if (view.Suggestion is { } s && (shown is null || (shown.From, shown.To) != (s.From, s.To)))
            suggestions++;
        shown = view.Suggestion;
        var lines = CoachText.Lines(view, data);
        var text = string.Join(" | ", new[] { lines.Current, lines.Status, lines.Next }.Concat(lines.Running).Concat([lines.Late.Count > 0 ? "Atrasado: " + string.Join("; ", lines.Late) : null, lines.Deviation, lines.Suggestion, lines.Alert, lines.Idle]).Where(s => s is not null));
        if (text == last)
            continue;
        last = text;
        Console.WriteLine($"{index,5} {BoCoach.Core.GameClock.Format(view.Time),6} pob {view.Population}/{view.PopulationCap}  {text}");
        if (args.Contains("--economy") && coach.Economy is { } e)
            Console.WriteLine($"        tasas {e.ObservedRates} · aldeanos {e.Villagers.PerResource} (efic {e.Villagers.Efficiency:0.00}) · demanda {e.Demand} · déficit {e.Deficit} · candidata {e.Candidate}");
    }

    Console.WriteLine();
    foreach (var step in coach.Tracker.Steps)
        Console.WriteLine($"  {step.Step.Id,-4} {step.State,-8} {step.Completed}/{step.Required} {(step.DoneAt is { } t ? BoCoach.Core.GameClock.Format(t) : ""),6}  {StepText.Describe(step.Step)}");
    Console.WriteLine($"  extras: {string.Join(", ", coach.Tracker.Extras.GroupBy(e => e.Event.EventId).Select(g => $"{g.Key}×{g.Count()}"))}");
    Console.WriteLine($"  sugerencias ({allocator}): {suggestions}");
    return 0;
}

// ---- bench: cuánto tarda cada parte de la lectura, para estimar la CPU en vivo ----
static async Task<int> Bench(string recording, string[] args)
{
    var session = RecordedSession.Load(recording);
    var hud = new HudReader(HudFonts.Load(Text(args, "--fonts", Path.Combine("data", "fonts"))));
    var ocr = BoCoach.Vision.Ocr.OcrReader.TryCreate() ?? throw new InvalidDataException("El OCR en inglés de Windows no está instalado.");
    var frames = session.Frames.Take(Option(args, "--frames", 300)).ToList();

    // Decodificar los PNG solo pasa en el replay: en vivo las regiones salen de la captura GDI.
    var watch = System.Diagnostics.Stopwatch.StartNew();
    var images = frames.Select(f => RegionNames.All.ToDictionary(r => r, r => session.Has(f.Index, r) ? session.Image(f.Index, r) : null)).ToList();
    var decode = watch.Elapsed.TotalMilliseconds / frames.Count;

    watch.Restart();
    foreach (var image in images)
        hud.Read(r => image[r]);
    var hudMs = watch.Elapsed.TotalMilliseconds / frames.Count;

    watch.Restart();
    var logs = images.Select(i => i[RegionNames.EventLog]).OfType<CapturedImage>().ToList();
    var withText = logs.Where(LogImage.HasText).ToList();
    var hasText = watch.Elapsed.TotalMilliseconds / frames.Count;

    // El lector real, captura por captura: solo pasa el OCR si el texto cambió.
    var reader = new EventLogReader(ocr, Catalog(args));
    watch.Restart();
    foreach (var log in logs)
        await reader.ReadAsync(log);
    var ocrTotal = watch.Elapsed.TotalMilliseconds;

    var ages = images.Select(i => i[RegionNames.Age]).OfType<CapturedImage>().Take(20).ToList();
    watch.Restart();
    foreach (var age in ages)
        await ocr.ReadAsync(age);
    var ageMs = ages.Count == 0 ? 0 : watch.Elapsed.TotalMilliseconds / ages.Count;

    var ocrPerFrame = ocrTotal / frames.Count;
    var live = hudMs + hasText + ocrPerFrame + ageMs / 4; // la edad se lee cada 2 s = 1 de cada 4 capturas
    Console.WriteLine($"{frames.Count} capturas ({withText.Count} con texto en el log)");
    Console.WriteLine($"  decodificar PNG (solo replay): {decode,6:0.0} ms por captura");
    Console.WriteLine($"  números del HUD:               {hudMs,6:0.0} ms");
    Console.WriteLine($"  ¿hay texto en el log?:         {hasText,6:0.0} ms");
    Console.WriteLine($"  log (máscara + OCR si cambió): {ocrPerFrame,6:0.0} ms en promedio ({reader.OcrCalls} OCR en {logs.Count} capturas, {(reader.OcrCalls == 0 ? 0 : ocrTotal / reader.OcrCalls):0.0} ms cada uno)");
    Console.WriteLine($"  OCR de la edad:                {ageMs,6:0.0} ms (cada 2 s)");
    Console.WriteLine($"  total en vivo (sin captura):   {live,6:0.0} ms por captura → {live / 500 / Environment.ProcessorCount * 100:0.0} % de CPU con {Environment.ProcessorCount} núcleos");
    return 0;
}

/// <summary>Las lecturas de cada captura: de un lecturas.csv ya guardado, o pasando la visión por la grabación.</summary>
static async IAsyncEnumerable<(int Frame, ScreenReading Reading)> Readings(string source, string[] args)
{
    if (File.Exists(source))
    {
        foreach (var logged in ScreenReadingLog.Load(source))
            yield return (logged.Frame, logged.Reading);
        yield break;
    }

    var session = RecordedSession.Load(source);
    var reader = Reader(session, args);
    foreach (var frame in session.Frames)
        yield return (frame.Index, (await reader.ReadAsync(Regions(session, frame.Index), frame.TimeMs / 1000.0)).ToReading());
}

static string Text(string[] args, string name, string fallback)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
}

static ScreenReader Reader(RecordedSession session, string[] args)
{
    var ocr = args.Contains("--no-log") ? null : BoCoach.Vision.Ocr.OcrReader.TryCreate();
    return new ScreenReader(HudFonts.Load(Text(args, "--fonts", Path.Combine("data", "fonts"))), ocr, Catalog(args));
}

// Frases del log, prefiriendo (ante una lectura dudosa) los eventos de las entidades de game_data, como la app.
static EventCatalog Catalog(string[] args, GameData? data = null) =>
    EventCatalog.Load(Text(args, "--events", Path.Combine("data", "events.json")))
        .PreferringEntitiesOf(data ?? GameDataLoader.LoadLatest(Text(args, "--game-data", Path.Combine("data", "game_data"))));

static Func<string, CapturedImage?> Regions(RecordedSession session, int frame) =>
    region => session.Has(frame, region) ? session.Image(frame, region) : null;
