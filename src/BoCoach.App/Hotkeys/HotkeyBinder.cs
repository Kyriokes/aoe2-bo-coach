namespace BoCoach.App.Hotkeys;

/// <summary>Lo que hace falta de Windows para un atajo global (<see cref="Overlay.GlobalHotkeys"/>; en los tests, uno falso).</summary>
public interface IHotkeyRegistrar
{
    /// <summary>Registra la combinación; devuelve su id, o null si Windows no la deja (la tiene otro programa).</summary>
    int? Register(HotkeyBinding binding, Action action);

    void Unregister(int id);
}

public enum ChangeResult
{
    Done,

    /// <summary>La tiene otra acción de la app (<see cref="HotkeyBinder.LastConflict"/>): no se tocó nada.</summary>
    UsedByOther,

    /// <summary>Windows no la dejó registrar: sigue la anterior.</summary>
    Rejected,
}

/// <summary>Registra en Windows los atajos del <see cref="HotkeyMap"/> y los cambia sin perder el anterior si falla.</summary>
public sealed class HotkeyBinder(IHotkeyRegistrar registrar, HotkeyMap map, IReadOnlyDictionary<string, Action> actions, bool includeDeveloper)
{
    private readonly Dictionary<string, int> _ids = [];

    /// <summary>La acción que tenía la combinación pedida en el último <see cref="ChangeResult.UsedByOther"/>.</summary>
    public HotkeyAction? LastConflict { get; private set; }

    private IEnumerable<HotkeyAction> Active => HotkeyActions.All.Where(a => (includeDeveloper || !a.Developer) && actions.ContainsKey(a.Id));

    /// <summary>Registra todas las del mapa; devuelve los ids que no se pudieron (otro programa o sin atajo).</summary>
    public IReadOnlyList<string> RegisterAll()
    {
        var missing = new List<string>();
        foreach (var action in Active)
        {
            if (map.Get(action.Id) is { } binding && registrar.Register(binding, actions[action.Id]) is { } id)
                _ids[action.Id] = id;
            else
                missing.Add(action.Id);
        }
        return missing;
    }

    /// <summary>Cambia la combinación de una acción; <paramref name="binding"/> null vuelve a la de fábrica.</summary>
    public ChangeResult Change(string id, HotkeyBinding? binding)
    {
        if (HotkeyActions.Find(id) is not { } action)
            return ChangeResult.Rejected;
        var target = binding ?? action.Default;
        if (map.UsedBy(target, id) is { } other)
        {
            LastConflict = other;
            return ChangeResult.UsedByOther;
        }
        if (map.Get(id) != target && Active.Contains(action))
        {
            var old = map.Get(id);
            if (_ids.Remove(id, out var oldId))
                registrar.Unregister(oldId);
            if (registrar.Register(target, actions[id]) is { } newId)
                _ids[id] = newId;
            else
            {
                if (old is { } previous && registrar.Register(previous, actions[id]) is { } back)
                    _ids[id] = back;
                return ChangeResult.Rejected;
            }
        }
        if (target == action.Default)
            map.Reset(id);
        else
            map.Set(id, target);
        return ChangeResult.Done;
    }

    /// <summary>Todas de fábrica (de a una no se puede si dos se intercambiaron); devuelve las que no se pudieron registrar.</summary>
    public IReadOnlyList<string> ResetAll()
    {
        foreach (var id in _ids.Values)
            registrar.Unregister(id);
        _ids.Clear();
        map.ResetAll();
        return RegisterAll();
    }
}
