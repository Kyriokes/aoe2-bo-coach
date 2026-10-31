namespace BoCoach.App.Coaching;

/// <summary>
/// D6: un reloj que vuelve para atrás más de <see cref="BackSeconds"/> en <see cref="BackReads"/> lecturas seguidas es
/// otra partida (sin pantalla de carga leída); una sola lectura para atrás puede ser un error de lectura. Se compara con
/// el reloj suavizado, no con la lectura cruda anterior: en la partida 20261031-140712 una captura leyó "09:06:10" por
/// "00:06:10" y las 3 siguientes (6:11, 6:12, 6:13) parecían "9 horas para atrás": el coach empezaba otra partida y
/// borraba lo hecho (el telar).
/// </summary>
public sealed class NewGameDetector
{
    public const double BackSeconds = 5;
    public const int BackReads = 3;

    private int _backwards;

    /// <param name="raw">El reloj tal cual se leyó en esta captura.</param>
    /// <param name="smoothed">El reloj suavizado.</param>
    public bool Check(double? raw, double? smoothed)
    {
        if (raw is not { } now || smoothed is not { } clock)
            return false;
        // El suavizado nunca deja que el reloj vuelva para atrás: una partida nueva se ve en la lectura cruda.
        if (now < clock - BackSeconds)
        {
            if (++_backwards < BackReads)
                return false;
            _backwards = 0;
            return true;
        }
        _backwards = 0;
        return false;
    }

    public void Reset() => _backwards = 0;
}
