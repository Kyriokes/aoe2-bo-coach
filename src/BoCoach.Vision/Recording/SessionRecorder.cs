using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Regions;

namespace BoCoach.Vision.Recording;

/// <summary>
/// Graba las regiones calibradas de una partida real, para poder correr después todo el pipeline sobre la
/// grabación (ReplaySession) sin volver a jugar. Cada grabación es una carpeta con:
/// <list type="bullet">
/// <item><c>session.json</c>: zona del juego en pantalla, regiones (fracciones y píxeles absolutos) e intervalo.</item>
/// <item><c>frames.csv</c>: número de captura y milisegundos desde el inicio.</item>
/// <item><c>NNNNNN_region.png</c>: una imagen por región y captura.</item>
/// </list>
/// </summary>
public sealed class SessionRecorder : IDisposable
{
    private readonly IScreenSource _source;
    private readonly RegionLayout _layout;
    private readonly TimeSpan _interval;
    private readonly Stopwatch _clock = new();
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private StreamWriter? _index;
    private PixelRect _area;

    public SessionRecorder(IScreenSource source, RegionLayout layout, string outputRoot, TimeSpan? interval = null)
    {
        _source = source;
        _layout = layout;
        OutputRoot = outputRoot;
        _interval = interval ?? TimeSpan.FromMilliseconds(500);
    }

    public static string DefaultOutputRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BoCoach", "recordings");

    public string OutputRoot { get; }
    public string? Folder { get; private set; }
    public int Frames { get; private set; }
    public TimeSpan Elapsed => _clock.Elapsed;
    public bool IsRecording => _loop is not null;

    /// <summary>Último error al capturar o escribir (la grabación sigue).</summary>
    public string? LastError { get; private set; }

    /// <summary>Crea la carpeta y escribe session.json, sin arrancar el temporizador (útil para tests).</summary>
    public void Open()
    {
        _area = _source.CaptureArea;
        Folder = Path.Combine(OutputRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        var suffix = 1;
        while (Directory.Exists(Folder))
            Folder = Path.Combine(OutputRoot, $"{DateTime.Now:yyyyMMdd-HHmmss}-{++suffix}");
        Directory.CreateDirectory(Folder);

        var session = new
        {
            schema_version = 1,
            started_at = DateTimeOffset.Now,
            area = new[] { _area.X, _area.Y, _area.Width, _area.Height },
            game_found = _source.GameFound,
            interval_ms = (int)_interval.TotalMilliseconds,
            regions = _layout.Regions.ToDictionary(
                r => r.Key,
                r =>
                {
                    var px = r.Value.ToPixels(_area);
                    return new
                    {
                        fraction = new[] { r.Value.X, r.Value.Y, r.Value.Width, r.Value.Height },
                        pixels = new[] { px.X, px.Y, px.Width, px.Height },
                    };
                }),
        };
        File.WriteAllText(Path.Combine(Folder, "session.json"),
            JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        _index = new StreamWriter(Path.Combine(Folder, "frames.csv")) { AutoFlush = true };
        _index.WriteLine("frame,time_ms");
        Frames = 0;
        _clock.Restart();
    }

    public void Start()
    {
        if (IsRecording)
            return;
        Open();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(_interval);
            do
            {
                CaptureFrame();
            }
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false));
        }, token);
    }

    /// <summary>Captura todas las regiones una vez y las escribe.</summary>
    public void CaptureFrame()
    {
        lock (_lock)
        {
            if (Folder is null || _index is null)
                throw new InvalidOperationException("Primero hay que abrir la grabación.");
            var frame = Frames;
            var time = (long)_clock.Elapsed.TotalMilliseconds;
            try
            {
                foreach (var (name, rect) in _layout.Regions)
                {
                    var image = _source.Capture(rect.ToPixels(_area));
                    Png.Write(image, Path.Combine(Folder, FileName(frame, name)));
                }
                _index.WriteLine($"{frame},{time}");
                Frames++;
            }
            catch (Exception e) when (e is IOException or ArgumentException)
            {
                LastError = e.Message;
            }
        }
    }

    public void Stop()
    {
        if (_cts is not null)
        {
            _cts.Cancel();
            try
            {
                _loop?.Wait();
            }
            catch (AggregateException e) when (e.InnerExceptions.All(x => x is OperationCanceledException))
            {
            }
            _cts.Dispose();
            _cts = null;
            _loop = null;
        }
        lock (_lock)
        {
            _index?.Dispose();
            _index = null;
        }
        _clock.Stop();
    }

    public void Dispose() => Stop();

    public static string FileName(int frame, string region) => $"{frame:D6}_{region}.png";
}
