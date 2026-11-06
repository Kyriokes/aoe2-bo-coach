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
    private double? _first;
    private double _firstTime;
    private int _advances;
    private double? _chain;
    private double _chainTime;
    private int _chainCount;

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

    /// <summary>
    /// El reloj de la partida: sube a lo sumo 3 s de juego por segundo real (velocidad del juego con margen), nunca baja
    /// (una partida nueva la ve <c>LiveCoach</c> con la lectura cruda; la única baja es corregir una hora mala que quedó
    /// aceptada, ver <see cref="ConfirmFirst"/>) y un salto se acepta si se repite 6 veces.
    /// </summary>
    /// En la partida 20261031-140712 la letra leyó "09:06:10" por "00:06:10" y, en pausa, la lectura se repitió: un
    /// salto de más de 10 minutos no se confirma nunca.
    public static ValueFilter GameClock() => new()
    {
        Window = 1, MaxRisePerSecond = 3, MaxDropPerSecond = 0, Slack = 1.5, ConfirmJump = 6, MaxConfirmedJump = 600, ConfirmFirst = true,
        LowerChainWinsOver = 3000,
    };

    /// <summary>
    /// Un salto (más allá de lo plausible) de más que esto no se confirma aunque se repita: es una lectura equivocada.
    /// Null, cualquiera se confirma con <see cref="ConfirmJump"/>.
    /// </summary>
    public double? MaxConfirmedJump { get; init; }

    /// <summary>
    /// La primera lectura no se toma sola: hace falta una siguiente que pueda seguirle, y mientras tanto no hay valor.
    /// Con el reloj, que nunca baja, una primera lectura mala ("09:06:10") quedaba clavada toda la partida.
    /// Igual, la misma lectura mala dos veces (en pausa, o con algo tapando el reloj al arrancar) se confirmaba: por eso,
    /// mientras el valor no subió <see cref="ConfirmJump"/> veces desde que se tomó, <see cref="ConfirmJump"/> lecturas
    /// descartadas seguidas que suben entre sí y quedan por debajo lo reemplazan. Una racha más alta no, nunca: las
    /// lecturas malas suman horas. Y después, una más baja solo si está más de <see cref="LowerChainWinsOver"/> por
    /// debajo: a mitad de partida una racha mala que avanza (un cuadro tapando la hora) no mueve el reloj.
    /// </summary>
    public bool ConfirmFirst { get; init; }

    /// <summary>
    /// Con <see cref="ConfirmFirst"/>, una cadena de lecturas que avanza más de esto por debajo de lo aceptado lo reemplaza
    /// aunque ya esté corroborado (null = nunca). Con el reloj, 50 minutos: en la práctica solo una hora mal leída da tanta
    /// diferencia (un dígito de los minutos, como mucho 50), y las lecturas malas suman horas. Si el cuadro que tapa la hora al arrancar dura más de unos 3,5 s, las lecturas
    /// malas que siguen avanzando corroboran la hora mala, y el reloj quedaba clavado toda la partida.
    /// </summary>
    public double? LowerChainWinsOver { get; init; }

    /// <summary>Valor suavizado actual, o null si todavía no hubo ninguna lectura válida.</summary>
    public double? Value => _window.Count == 0 ? null : Median();

    public double? Add(double? reading, double time)
    {
        // Desde la última lectura aceptada, no desde la captura anterior: si se descartaron o no se leyeron
        // capturas, el margen crece, y un valor que sigue avanzando (el reloj después de un tirón) se destraba.
        var dt = _lastAcceptedTime is { } previous ? Math.Max(0, time - previous) : 0;
        if (reading is not { } value)
            return Value;

        if (ConfirmFirst && _lastAccepted is null && !(_first is { } first && Plausible(value - first, Math.Max(0, time - _firstTime))))
        {
            (_first, _firstTime) = (value, time);
            return Value;
        }

        if (_lastAccepted is { } last && !Plausible(value - last, dt))
        {
            if (!LowerChainWins(value, time))
            {
                if (MaxConfirmedJump is { } max && Math.Abs(value - last) > max + (MaxRisePerSecond ?? 0) * dt + Slack)
                    return Value;
                if (_pending is { } pending && Math.Abs(pending - value) < 1e-9)
                    _pendingCount++;
                else
                    (_pending, _pendingCount) = (value, 1);
                if (_pendingCount < ConfirmJump)
                    return Value;
            }
            _window.Clear(); // el salto era real: se arranca de nuevo desde el valor nuevo
            (_advances, _chain, _chainCount) = (0, null, 0);
        }
        else if (_lastAccepted is { } accepted && value > accepted)
        {
            // Sube desde lo aceptado: lo corrobora, y lo descartado hasta acá deja de ser una cadena.
            _advances++;
            (_chain, _chainCount) = (null, 0);
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

    /// <summary>
    /// Con <see cref="ConfirmFirst"/>, si las lecturas descartadas seguidas que quedan por debajo de lo aceptado forman
    /// una cadena que sube de a poco (cada una plausible desde la anterior) de <see cref="ConfirmJump"/> valores, lo
    /// aceptado era la lectura mala, siempre que todavía no esté corroborado o que la cadena quede más de
    /// <see cref="LowerChainWinsOver"/> por debajo. Una igual a la anterior de la cadena no suma ni la corta (en pausa no
    /// sube). Una por arriba de lo aceptado tampoco: las lecturas malas del reloj suman horas ("1:35:22" por "0:35:22",
    /// "01:41:01" por "00:41:06"), y una racha más alta (un cuadro tapando la hora unos segundos) pisaba un reloj bueno
    /// recién tomado.
    /// </summary>
    private bool LowerChainWins(double value, double time)
    {
        if (!ConfirmFirst || _lastAccepted is not { } accepted || value >= accepted)
            return false;
        if (_chain is { } previous && Plausible(value - previous, Math.Max(0, time - _chainTime)))
        {
            if (value > previous)
                (_chain, _chainTime, _chainCount) = (value, time, _chainCount + 1);
        }
        else
        {
            (_chain, _chainTime, _chainCount) = (value, time, 1);
        }
        return _chainCount >= ConfirmJump
            && (_advances < ConfirmJump || LowerChainWinsOver is { } gap && accepted - value > gap);
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
