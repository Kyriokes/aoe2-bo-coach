namespace BoCoach.App.Hotkeys;

/// <summary>
/// La combinación de cada atajo: la de fábrica salvo lo que el jugador cambió (settings.json, <c>hotkeys</c>). No
/// registra nada en Windows (eso lo hace HotkeyBinder); también da el texto que muestran la app y el overlay.
/// </summary>
public sealed class HotkeyMap
{
    private readonly Dictionary<string, HotkeyBinding?> _current = [];

    /// <summary>El mapa con que corre la app (la ventana principal lo reemplaza al abrir); por defecto, las de fábrica.</summary>
    public static HotkeyMap Current { get; set; } = new();

    /// <summary>Cambió alguna combinación (para guardar y para refrescar los textos).</summary>
    public event Action? Changed;

    /// <summary>
    /// Lo que no sirve de <paramref name="overrides"/> (id desconocido, texto que no se entiende, combinación repetida)
    /// se ignora. Primero quedan las de fábrica de las acciones sin cambio; después, en el orden de la tabla, las
    /// cambiadas que no repiten; una que repite vuelve a su fábrica si está libre, y si no queda sin atajo.
    /// </summary>
    public HotkeyMap(IReadOnlyDictionary<string, string>? overrides = null)
    {
        var wanted = HotkeyActions.All.ToDictionary(a => a.Id,
            a => overrides is not null && overrides.TryGetValue(a.Id, out var text) ? HotkeyBinding.Parse(text) : null);
        var used = new HashSet<HotkeyBinding>();
        foreach (var action in HotkeyActions.All.Where(a => wanted[a.Id] is null))
            _current[action.Id] = used.Add(action.Default) ? action.Default : null;
        var fallback = new List<HotkeyAction>();
        foreach (var action in HotkeyActions.All.Where(a => wanted[a.Id] is not null))
        {
            if (used.Add(wanted[action.Id]!.Value))
                _current[action.Id] = wanted[action.Id];
            else
                fallback.Add(action);
        }
        foreach (var action in fallback)
            _current[action.Id] = used.Add(action.Default) ? action.Default : null;
    }

    public HotkeyBinding? Get(string id) => _current.GetValueOrDefault(id);

    /// <summary>La combinación para mostrar ("Ctrl+Alt+→"), o "sin atajo".</summary>
    public string Text(string id) => Get(id)?.Display ?? "sin atajo";

    /// <summary>La otra acción que ya tiene esa combinación, o null.</summary>
    public HotkeyAction? UsedBy(HotkeyBinding binding, string exceptId) =>
        HotkeyActions.All.FirstOrDefault(a => a.Id != exceptId && Get(a.Id) == binding);

    /// <summary>Pone la combinación sin mirar choques (los controla quien llama).</summary>
    public void Set(string id, HotkeyBinding binding)
    {
        _current[id] = binding;
        Changed?.Invoke();
    }

    public void Reset(string id)
    {
        _current[id] = HotkeyActions.Find(id)?.Default;
        Changed?.Invoke();
    }

    public void ResetAll()
    {
        foreach (var action in HotkeyActions.All)
            _current[action.Id] = action.Default;
        Changed?.Invoke();
    }

    /// <summary>Lo distinto de fábrica, para settings.json ("Ctrl+Alt+Right"). Una acción sin atajo no se guarda.</summary>
    public IReadOnlyDictionary<string, string> Overrides => HotkeyActions.All
        .Where(a => Get(a.Id) is { } binding && binding != a.Default)
        .ToDictionary(a => a.Id, a => Get(a.Id)!.Value.Storage);
}
