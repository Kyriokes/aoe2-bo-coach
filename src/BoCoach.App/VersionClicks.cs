namespace BoCoach.App;

/// <summary>Cuenta clics sobre el número de versión: <c>count</c> seguidos dentro de la ventana prenden o apagan el modo desarrollador.</summary>
public sealed class VersionClicks(int count = 5, TimeSpan? window = null)
{
    private readonly TimeSpan _window = window ?? TimeSpan.FromSeconds(3);
    private readonly Queue<DateTime> _clicks = [];

    /// <summary>True cuando este clic completa la cuenta; después vuelve a contar de cero.</summary>
    public bool Click(DateTime now)
    {
        _clicks.Enqueue(now);
        while (_clicks.Count > 0 && now - _clicks.Peek() > _window)
            _clicks.Dequeue();
        if (_clicks.Count < count)
            return false;
        _clicks.Clear();
        return true;
    }
}
