namespace BoCoach.Vision.Smoothing;

/// <summary>
/// Suaviza un valor leído del HUD captura por captura:
/// <list type="bullet">
/// <item>Ignora las lecturas que no sirven (null: número cortado, carácter desconocido, nada leído).</item>
/// <item>Descarta los saltos imposibles (subir o bajar más de lo que puede cambiar en ese tiempo), salvo que el
/// valor nuevo se repita varias capturas seguidas: entonces el salto era real.</item>
/// <item>Devuelve la mediana de las últimas lecturas aceptadas.</item>
/// </list>
/// </summary>
public sealed class ValueFilter
{
    private readonly Queue<double> _window = new();
    private double? _lastAcceptedTime;
    private double? _lastAccepted;
    private double? _pending;
    private int _pendingCount;

    /// <summary>Cuántas lecturas aceptadas entran en la mediana.</summary>
    public int Window { get; init; } = 5;

    /// <summary>Lo máximo que puede subir por segundo (null = sin límite).</summary>
    public double? MaxRisePerSecond { get; init; }

    /// <summary>Lo máximo que puede bajar por segundo (null = sin límite; 0 = nunca baja, como el reloj).</summary>
    public double? MaxDropPerSecond { get; init; }

    /// <summary>Margen fijo que se suma a los límites (tolera una lectura algo atrasada o adelantada).</summary>
    public double Slack { get; init; }

    /// <summary>Capturas seguidas con el mismo salto para aceptarlo como real.</summary>
    public int ConfirmJump { get; init; } = 3;

    /// <summary>
    /// Si baja más que esto de golpe (un gasto), la mediana arranca de nuevo desde el valor nuevo, así el gasto
    /// se ve enseguida en vez de quedar tapado por las lecturas anteriores (null = nunca).
    /// </summary>
    public double? ResetOnDrop { get; init; }

    /// <summary>Valor suavizado actual, o null si todavía no hubo ninguna lectura válida.</summary>
    public double? Value => _window.Count == 0 ? null : Median();

    public double? Add(double? reading, double time)
    {
        // Desde la última lectura aceptada, no desde la captura anterior: si se descartaron o no se leyeron
        // capturas, el margen crece, y un valor que sigue avanzando (el reloj después de un tirón) se destraba.
        var dt = _lastAcceptedTime is { } previous ? Math.Max(0, time - previous) : 0;
        if (reading is not { } value)
            return Value;

        if (_lastAccepted is { } last && !Plausible(value - last, dt))
        {
            if (_pending is { } pending && Math.Abs(pending - value) < 1e-9)
                _pendingCount++;
            else
                (_pending, _pendingCount) = (value, 1);
            if (_pendingCount < ConfirmJump)
                return Value;
            _window.Clear(); // el salto era real: se arranca de nuevo desde el valor nuevo
        }

        if (ResetOnDrop is { } drop && _lastAccepted is { } before && before - value > drop)
            _window.Clear();
        _pending = null;
        _pendingCount = 0;
        _lastAccepted = value;
        _lastAcceptedTime = time;
        _window.Enqueue(value);
        while (_window.Count > Window)
            _window.Dequeue();
        return Value;
    }

    private bool Plausible(double change, double dt)
    {
        if (change > 0 && MaxRisePerSecond is { } rise)
            return change <= rise * dt + Slack;
        if (change < 0 && MaxDropPerSecond is { } drop)
            return -change <= drop * dt + Slack;
        return true;
    }

    /// <summary>Mediana; con cantidad par, el valor del medio de arriba (los valores del HUD son enteros).</summary>
    private double Median() => _window.Order().ElementAt(_window.Count / 2);
}
