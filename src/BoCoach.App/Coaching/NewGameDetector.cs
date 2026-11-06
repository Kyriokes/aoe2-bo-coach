namespace BoCoach.App.Coaching;

/// <summary>
/// D6: un reloj que vuelve para atrás más de <see cref="BackSeconds"/>, hasta cerca del cero (<see cref="NewGameSeconds"/>),
/// en <see cref="BackReads"/> lecturas seguidas es otra partida (sin pantalla de carga leída); una sola lectura para atrás puede ser un error de lectura. Se compara con
/// el reloj suavizado, no con la lectura cruda anterior: en la partida 20261031-140712 una captura leyó "09:06:10" por
/// "00:06:10" y las 3 siguientes (6:11, 6:12, 6:13) parecían "9 horas para atrás": el coach empezaba otra partida y
/// borraba lo hecho (el telar).
/// </summary>
public sealed class NewGameDetector
{
    public const double BackSeconds = 5;
    public const int BackReads = 3;

    /// <summary>
    /// Una partida nueva se reconoce en sus primeros segundos: el reloj crudo tiene que estar cerca del cero. Un reloj
    /// que vuelve 47 s para atrás a las 26:52 es una lectura mala (partida 20261101-165020: un cuadro del juego tapó el
    /// último dígito y "00:26:5" se leyó 26:05 tres veces seguidas), no otra partida.
    /// </summary>
    public const double NewGameSeconds = 180;

    private int _backwards;

    /// <param name="raw">El reloj tal cual se leyó en esta captura.</param>
    /// <param name="smoothed">El reloj suavizado.</param>
    public bool Check(double? raw, double? smoothed)
    {
        if (MayBeNew(raw, smoothed))
        {
            if (++_backwards < BackReads)
                return false;
            _backwards = 0;
            return true;
        }
        _backwards = 0;
        return false;
    }

    /// <summary>
    /// Esta lectura puede ser de otra partida: el reloj crudo volvió para atrás, cerca del cero. Hasta la lectura
    /// <see cref="BackReads"/> no se sabe si es otra partida o una lectura mala.
    /// </summary>
    public static bool MayBeNew(double? raw, double? smoothed) =>
        // El suavizado no deja que el reloj vuelva para atrás (solo para corregir una hora mala): una partida nueva se ve en la lectura cruda.
        raw is { } now && smoothed is { } clock && now < clock - BackSeconds && now <= NewGameSeconds;

    public void Reset() => _backwards = 0;
}
