namespace BoCoach.Core.Engine;

/// <summary>
/// Cuánto vale lo que dice una fuente (spec 2026-11-02, creencia del estado). Ante una contradicción gana la más
/// fuerte: una creencia de fuerza mayor hace imposible lo que afirma una evidencia más débil (la cola muestra arqueros
/// pero el log dice que se está en la Edad Oscura, donde no hay galerías).
/// </summary>
public enum Strength
{
    /// <summary>Deducido de otra cosa: el puntaje, la población menos los aldeanos.</summary>
    Inferred = 1,

    /// <summary>Visto en pantalla pero sin aviso: la cola, el tope de población, el ritmo de las unidades.</summary>
    Observed = 2,

    /// <summary>Avisado por el log, o lo que el juego da por regla (las tecnologías gratis de la civ).</summary>
    Announced = 3,

    /// <summary>Leído tal cual del HUD.</summary>
    Direct = 4,
}

/// <summary>De dónde salió una evidencia.</summary>
public enum EvidenceSource
{
    Log,
    Hud,
    Civ,
    Queue,
    PopulationCap,
    Production,
    Population,
    Score,
}

/// <summary>
/// Algo que terminó según la creencia (un edificio, una unidad, una tecnología), con la fuente que lo dijo. Las que no
/// salieron del log son sintéticas, como los eventos que el motor arma por otras señales.
/// </summary>
public sealed record Completion(string EventId, EvidenceSource Source, Strength Strength)
{
    public bool Synthetic => Source != EvidenceSource.Log;
}
