using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace BoCoach.App.Coaching;

/// <summary>El consumo de la app en un CSV: CPU, RAM y lo que tarda cada captura, una línea cada 2 s.</summary>
public sealed class UsageCsv : IDisposable
{
    private const double EverySeconds = 2;

    private readonly StreamWriter _log;
    private readonly Func<double> _clock;
    private readonly double _start;
    private double _last = double.NegativeInfinity;

    /// <param name="clock">Los segundos que van pasando; por defecto, el reloj de verdad.</param>
    public UsageCsv(string path, Func<double>? clock = null)
    {
        if (Path.GetDirectoryName(path) is { Length: > 0 } folder)
            Directory.CreateDirectory(folder);
        _log = new StreamWriter(path) { AutoFlush = true };
        _log.WriteLine("segundos,cpu_porcentaje,ram_mb,ms_por_captura");
        if (clock is null)
        {
            var watch = Stopwatch.StartNew();
            clock = () => watch.Elapsed.TotalSeconds;
        }
        _clock = clock;
        _start = clock();
    }

    public void Add(CoachFrame frame)
    {
        var now = _clock();
        if (now - _last < EverySeconds)
            return;
        _last = now;
        _log.WriteLine(string.Join(",",
            ((int)(now - _start)).ToString(CultureInfo.InvariantCulture),
            frame.Usage.CpuPercent.ToString("0.00", CultureInfo.InvariantCulture),
            frame.Usage.MemoryMb.ToString("0.0", CultureInfo.InvariantCulture),
            frame.FrameMs.ToString("0.0", CultureInfo.InvariantCulture)));
    }

    public void Dispose() => _log.Dispose();
}

/// <summary>
/// Modo desarrollador: el consumo de cada partida en su propio CSV, para comparar con datos de una partida real (por
/// ejemplo, mirar la pantalla más seguido). Queda en la PC: no va en el registro de partida. Si no se puede escribir,
/// sigue de largo: nunca le corta nada a la partida.
/// </summary>
public sealed class GameUsageLog(Func<bool> enabled, Func<string> newPath, Func<double>? clock = null)
{
    private UsageCsv? _csv;
    private bool _failed;

    public void OnFrame(CoachFrame frame, bool inGame)
    {
        if (!inGame)
        {
            Close();
            _failed = false;
            return;
        }
        if (_failed || !enabled())
            return;
        try
        {
            (_csv ??= new UsageCsv(newPath(), clock)).Add(frame);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _failed = true;
            Close();
        }
    }

    public void OnCoachStopped()
    {
        Close();
        _failed = false;
    }

    private void Close()
    {
        try
        {
            _csv?.Dispose();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // El archivo queda como esté.
        }
        _csv = null;
    }
}
