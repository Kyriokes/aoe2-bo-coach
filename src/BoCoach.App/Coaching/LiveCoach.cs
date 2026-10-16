using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Hud;
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
public sealed record CoachFrame(
    CoachView View,
    HudState? Hud,
    double SecondsWithoutClock,
    double FrameMs,
    ProcessUsage Usage,
    bool Paused,
    bool Ended,
    string? Error,
    PixelRect Area);

/// <summary>
/// El coach en vivo: cada <see cref="IFrameSource.Interval"/> toma una captura, la lee (Vision), arma el snapshot
/// y actualiza el <see cref="Coach"/>. Corre en segundo plano; los atajos (avanzar, retroceder, saltear) llegan
/// desde la ventana y se aplican con un lock. <see cref="Updated"/> se dispara fuera del hilo de la interfaz.
/// </summary>
public sealed class LiveCoach : IDisposable
{
    private readonly object _gate = new();
    private readonly Coach _coach;
    private readonly SnapshotBuilder _builder;
    private readonly ScreenReader _reader;
    private readonly UsageMeter _usage = new();
    private CancellationTokenSource? _cancel;
    private HudState? _hud;
    private double? _lastClockAt;
    private double _now;
    private double _frameMs;
    private bool _ended;
    private string? _error;
    private PixelRect _area;

    public LiveCoach(BuildOrder bo, GameData data, EventCatalog events, HudFonts fonts, OcrReader? ocr, IFrameSource frames)
    {
        BuildOrder = bo;
        Frames = frames;
        _coach = new Coach(bo, data, events);
        _builder = new SnapshotBuilder(data);
        _reader = new ScreenReader(fonts, ocr, events);
    }

    public BuildOrder BuildOrder { get; }
    public IFrameSource Frames { get; }

    /// <summary>En pausa no se captura nada (por ejemplo, entre partidas).</summary>
    public bool Paused { get; set; }

    public bool Running => _cancel is { IsCancellationRequested: false };

    public event Action<CoachFrame>? Updated;

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
        if (Frames.Next() is not { } frame)
        {
            _ended = true;
            Publish();
            return false;
        }

        var watch = Stopwatch.StartNew();
        try
        {
            var hud = await _reader.ReadAsync(frame.Region, frame.RealTime);
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
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or COMException)
        {
            _error = e.Message;
        }
        _frameMs = watch.Elapsed.TotalMilliseconds;
        Publish();
        return true;
    }

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
        Updated?.Invoke(new CoachFrame(view, _hud, withoutClock, _frameMs, _usage.Sample(), Paused, _ended, _error, _area));
    }
}
