using BoCoach.Core.Economy;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Hud;
using BoCoach.Vision.Loading;
using BoCoach.Vision.Ocr;

namespace BoCoach.App.Coaching;

/// <summary>CPU (porcentaje del total de la máquina, como el Administrador de tareas) y memoria del proceso.</summary>
public sealed record ProcessUsage(double CpuPercent, double MemoryMb)
{
    public override string ToString() => $"CPU {CpuPercent:0.0} % · RAM {MemoryMb:0} MB";
}

/// <summary>
/// Mide el uso del proceso cada 2 segundos. La memoria es la privada en uso (la columna "Memoria" del Administrador
/// de tareas); el working set total suma las DLL compartidas con otros programas y da bastante más.
/// </summary>
public sealed class UsageMeter
{
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _lastCpu;
    private TimeSpan _lastAt;

    public ProcessUsage Last { get; private set; } = new(0, 0);

    public ProcessUsage Sample()
    {
        var now = _clock.Elapsed;
        if (now - _lastAt < TimeSpan.FromSeconds(2) && Last.MemoryMb > 0)
            return Last;
        _process.Refresh();
        var cpu = _process.TotalProcessorTime;
        var percent = _lastAt == TimeSpan.Zero ? 0 : (cpu - _lastCpu) / (now - _lastAt) / Environment.ProcessorCount * 100;
        (_lastCpu, _lastAt) = (cpu, now);
        return Last = new ProcessUsage(percent, PrivateWorkingSet() / 1024.0 / 1024.0);
    }

    private long PrivateWorkingSet()
    {
        var counters = new ProcessMemoryCountersEx2 { Size = (uint)Marshal.SizeOf<ProcessMemoryCountersEx2>() };
        return GetProcessMemoryInfo(_process.Handle, ref counters, counters.Size)
            ? (long)counters.PrivateWorkingSetSize
            : _process.WorkingSet64;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCountersEx2
    {
        public uint Size;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
        public nuint PrivateUsage;
        public nuint PrivateWorkingSetSize;
        public ulong SharedCommitUsage;
    }

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfo(IntPtr process, ref ProcessMemoryCountersEx2 counters, uint size);
}

/// <summary>Lo que el loop publica después de cada captura (o de una corrección con atajo).</summary>
/// <param name="SecondsWithoutClock">Hace cuánto (en segundos reales) que no se lee el reloj del juego.</param>
/// <param name="FrameMs">Cuánto tardó en procesarse la última captura.</param>
/// <param name="Ended">Se terminó la grabación (en modo replay).</param>
/// <param name="Area">Zona del juego en la pantalla en la última captura.</param>
/// <param name="StartOffset">Segundos del reloj del juego antes de que el jugador empezara a jugar (ver <see cref="SnapshotBuilder.StartOffset"/>).</param>
/// <param name="HudScale">Tamaño del HUD respecto de 1080p: las franjas de <see cref="HudState.ModBands"/> están medidas a 1080p.</param>
/// <param name="Loading">Lo leído en la última pantalla de carga (la de esta partida), o null.</param>
/// <param name="Me">El jugador de la pantalla de carga con el nombre puesto en la app, o null.</param>
/// <param name="Civ">La civ con que corre el coach (clave de game_data, o "any").</param>
public sealed record CoachFrame(
    CoachView View,
    HudState? Hud,
    double SecondsWithoutClock,
    double FrameMs,
    ProcessUsage Usage,
    bool Paused,
    bool Ended,
    string? Error,
    PixelRect Area,
    double StartOffset = 0,
    double HudScale = 1,
    LoadingReading? Loading = null,
    LoadingPlayer? Me = null,
    string Civ = "any");

/// <summary>
/// El coach en vivo: cada <see cref="IFrameSource.Interval"/> toma una captura, la lee (Vision), arma el snapshot
/// y actualiza el <see cref="Coach"/>. Corre en segundo plano; los atajos (avanzar, retroceder, saltear) llegan
/// desde la ventana y se aplican con un lock. <see cref="Updated"/> se dispara fuera del hilo de la interfaz.
/// Mientras no se ve el reloj, mira cada tanto si está la pantalla de carga: al verla empieza la partida de cero
/// (el coach, el armado de snapshots y la lectura del HUD), con la civ del jugador si encontró su nombre.
/// </summary>
public sealed class LiveCoach : IDisposable
{
    /// <summary>Cada cuánto se mira el título de la pantalla de carga mientras no se ve el reloj.</summary>
    public static readonly TimeSpan LoadingInterval = TimeSpan.FromSeconds(2);

    /// <summary>Segundos de partida a partir de los que vale la pena el informe (D5).</summary>
    public const double MinReportSeconds = 60;

    /// <summary>Cuánto se espera sin reloj antes de mirar la pantalla de carga (lo que tarda el overlay en bajar).</summary>
    public static readonly TimeSpan LoadingDelay = TimeSpan.FromSeconds(1);

    private readonly object _gate = new();
    private readonly GameData _data;
    private readonly HudFonts _fonts;
    private readonly OcrReader? _ocr;
    private readonly EconomySettings? _economy;
    private readonly LoadingScreenReader? _loading;
    private Coach _coach;
    private SnapshotBuilder _builder;
    private ScreenReader _reader;
    private readonly UsageMeter _usage = new();
    private CancellationTokenSource? _cancel;
    private HudState? _hud;
    private double? _lastClockAt;
    private double _now;
    private double _frameMs;
    private bool _ended;
    private string? _error;
    private PixelRect _area;
    private double? _lastLoadingTry;
    private double? _noClockSince;
    private bool _loadingDone;
    private LoadingReading? _loadingReading;
    private LoadingPlayer? _me;

    /// <param name="playerName">El nombre del jugador en el juego, para encontrar su renglón en la pantalla de carga.</param>
    /// <param name="language">Idioma del juego (<see cref="GameLanguage.Code"/>), para los nombres de las civs.</param>
    public LiveCoach(BuildOrder bo, GameData data, EventCatalog events, HudFonts fonts, OcrReader? ocr, IFrameSource frames,
        EconomySettings? economy = null, string? playerName = null, string language = "en")
    {
        BuildOrder = bo;
        Frames = frames;
        Events = events;
        PlayerName = playerName;
        _data = data;
        _fonts = fonts;
        _ocr = ocr;
        _economy = economy;
        _loading = ocr is null ? null : new LoadingScreenReader(ocr, data, language);
        Civ = bo.Civ;
        (_coach, _builder, _reader) = NewGame(bo);
    }

    /// <summary>La BO con que se empezó (con la civ elegida en la ventana).</summary>
    public BuildOrder BuildOrder { get; }
    public IFrameSource Frames { get; }

    public string? PlayerName { get; }

    /// <summary>La civ con que corre el coach: la de la BO, o la del jugador desde la última pantalla de carga.</summary>
    public string Civ { get; private set; }

    /// <summary>Las frases del log con que lee (las del idioma del juego).</summary>
    public EventCatalog Events { get; }

    /// <summary>En pausa no se captura nada (por ejemplo, entre partidas).</summary>
    public bool Paused { get; set; }

    public bool Running => _cancel is { IsCancellationRequested: false };

    public event Action<CoachFrame>? Updated;

    /// <summary>Terminó una partida porque empezó otra (pantalla de carga): su informe (D5).</summary>
    public event Action<GameReport>? GameFinished;

    /// <summary>El informe de la partida en curso (D5).</summary>
    public GameReport Report()
    {
        lock (_gate)
            return _coach.Report();
    }

    public void Start()
    {
        if (Running)
            return;
        _cancel = new CancellationTokenSource();
        var token = _cancel.Token;
        _ = Task.Run(() => RunAsync(token), token);
    }

    public void Stop() => _cancel?.Cancel();

    public void Dispose() => Stop();

    public void Advance() => Correct(c => c.Advance());

    public void Back() => Correct(c => c.Back());

    public void Skip() => Correct(c => c.Skip());

    /// <summary>Procesa una captura. Público para poder probar el loop sin temporizador.</summary>
    public async Task<bool> StepAsync()
    {
        var watch = Stopwatch.StartNew();
        try
        {
            if (Frames.Next() is not { } frame)
            {
                _ended = true;
                Publish();
                return false;
            }

            if (await LoadingScreenAsync(frame) is { } reading)
                StartGame(reading);
            var hud = await _reader.ReadAsync(frame.Region, frame.RealTime);
            if (hud.ClockRead)
                _loadingDone = false;
            var snapshot = _builder.Build(hud.ToReading());
            lock (_gate)
            {
                if (snapshot is not null)
                    _coach.Update(snapshot);
            }
            _hud = hud;
            _now = frame.RealTime;
            _area = frame.Area;
            if (hud.ClockRead)
                _lastClockAt = frame.RealTime;
            _error = null;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Cualquier error de una captura (de la lectura o del coach) se muestra y se sigue con la próxima. Antes
            // solo se atrapaban los de lectura: cualquier otro cortaba el loop en silencio y el overlay quedaba
            // congelado con la última captura. Los inesperados llevan el tipo, para poder buscarlos.
            _error = e is ArgumentException or InvalidOperationException or IOException or COMException
                ? e.Message
                : $"{e.GetType().Name}: {e.Message}";
        }
        _frameMs = watch.Elapsed.TotalMilliseconds;
        Publish();
        return true;
    }

    /// <summary>
    /// La pantalla de carga, si está: sin reloj en la captura anterior, se lee el panel de jugadores cada
    /// <see cref="LoadingInterval"/> (una pasada del OCR). Una vez leída no se vuelve a leer hasta que aparezca el
    /// reloj (la pantalla dura varios segundos).
    /// </summary>
    private async Task<LoadingReading?> LoadingScreenAsync(Frame frame)
    {
        if (_hud?.ClockRead == true)
            _noClockSince = null;
        else
            _noClockSince ??= frame.RealTime;
        // Se espera un poco sin reloj: el overlay baja para no tapar el mapa de la pantalla de carga.
        if (_loading is null || frame.Screen is not { } screen || _loadingDone || _noClockSince is not { } since
            || frame.RealTime - since < LoadingDelay.TotalSeconds
            || _lastLoadingTry is { } last && frame.RealTime - last < LoadingInterval.TotalSeconds)
            return null;
        _lastLoadingTry = frame.RealTime;
        if (screen(LoadingScreen.Panel(frame.Area.Width, frame.Area.Height)) is not { } panel || await _loading.ReadPanelAsync(panel) is not { } reading)
            return null;
        // El mapa: con el overlay abajo (sin reloj), el panel de la derecha queda a la vista.
        return screen(LoadingScreen.Map(frame.Area.Width, frame.Area.Height)) is { } map
            ? reading with { Map = await _loading.ReadMapAsync(map) }
            : reading;
    }

    /// <summary>Empieza una partida nueva: todo de cero, con la civ del jugador si se lo encontró en la lista.</summary>
    private void StartGame(LoadingReading reading)
    {
        _loadingDone = true;
        var me = reading.Find(PlayerName);
        var bo = me?.Civ is { } civ ? BuildOrder with { Civ = civ } : BuildOrder;
        var finished = Report();
        if (finished.Length >= MinReportSeconds)
            GameFinished?.Invoke(finished);
        var (coach, builder, reader) = NewGame(bo);
        // D3: el renglón del jugador en el panel de puntajes es el de su color (su número en la pantalla de carga).
        coach.Signals.PlayerSlot = me?.Slot;
        coach.Signals.AllySlots = me is null ? new HashSet<int>() : reading.Allies(me).Select(p => p.Slot).ToHashSet();
        lock (_gate)
        {
            (_coach, _builder, _reader) = (coach, builder, reader);
            (_loadingReading, _me, Civ) = (reading, me, bo.Civ);
            _hud = null;
        }
    }

    private (Coach, SnapshotBuilder, ScreenReader) NewGame(BuildOrder bo) => (
        new Coach(bo, _data, Events, economy: _economy),
        new SnapshotBuilder(_data, _data.Civ(bo.Civ)),
        new ScreenReader(_fonts, _ocr, Events));

    private async Task RunAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(Frames.Interval);
        try
        {
            do
            {
                if (Paused)
                {
                    Publish();
                    continue;
                }
                if (!await StepAsync())
                    return;
            }
            while (await timer.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Correct(Func<Coach, CoachView> action)
    {
        lock (_gate)
            action(_coach);
        Publish();
    }

    private void Publish()
    {
        CoachView view;
        lock (_gate)
            view = _coach.View;
        var withoutClock = _lastClockAt is { } at ? _now - at : _now;
        Updated?.Invoke(new CoachFrame(view, _hud, withoutClock, _frameMs, _usage.Sample(), Paused, _ended, _error, _area, _builder.StartOffset,
            Frames.HudScale, _loadingReading, _me, Civ));
    }
}
