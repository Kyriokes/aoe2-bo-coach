using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BoCoach.App.Hotkeys;

/// <summary>La ventana "Atajos": un renglón por acción; se cambia una haciendo clic y apretando la combinación nueva.</summary>
public sealed partial class HotkeysViewModel
{
    private readonly HotkeyBinder _binder;
    private readonly HotkeyMap _map;

    public HotkeysViewModel(HotkeyBinder binder, HotkeyMap map, bool includeDeveloper)
    {
        (_binder, _map) = (binder, map);
        Rows = HotkeyActions.All.Where(a => includeDeveloper || !a.Developer).Select(a => new HotkeyRowViewModel(this, a)).ToList();
        map.Changed += Refresh;
        Refresh();
    }

    public IReadOnlyList<HotkeyRowViewModel> Rows { get; }

    /// <summary>
    /// Una tecla apretada mientras el renglón <paramref name="id"/> espera la combinación nueva. Devuelve true si la
    /// espera terminó (se cambió, chocó o se canceló con Escape); false si sigue esperando.
    /// </summary>
    public bool Capture(string id, ModifierKeys modifiers, Key key)
    {
        var row = Rows.First(r => r.Id == id);
        if (!row.Capturing)
            return true;
        if (key == Key.Escape)
        {
            _binder.RegisterAll();
            row.Stop(null, false);
            return true;
        }
        if (HotkeyBinding.IsModifier(key))
            return false;
        var binding = new HotkeyBinding(modifiers, key);
        if (!binding.IsValid)
        {
            row.Message = "Tiene que llevar Ctrl, Alt o Win";
            row.IsError = true;
            return false;
        }
        _binder.RegisterAll();
        Apply(row, binding);
        return true;
    }

    [RelayCommand]
    private void ResetAll()
    {
        var missing = _binder.ResetAll();
        foreach (var row in Rows)
            row.Stop(missing.Contains(row.Id) ? $"Otro programa ya usa {_map.Text(row.Id)}" : null, missing.Contains(row.Id));
    }

    /// <summary>Al cerrar la ventana: si un renglón esperaba la combinación, se cancela y los atajos vuelven.</summary>
    public void CancelCapture()
    {
        if (!Rows.Any(r => r.Capturing))
            return;
        _binder.RegisterAll();
        foreach (var row in Rows.Where(r => r.Capturing))
            row.Stop(null, false);
    }

    internal void StartCapture(HotkeyRowViewModel row)
    {
        if (Rows.Any(r => r.Capturing))
            _binder.RegisterAll();
        foreach (var other in Rows.Where(r => r.Capturing))
            other.Stop(null, false);
        _binder.Suspend();
        row.Capturing = true;
        row.Message = null;
        row.IsError = false;
        row.Text = "Apretá la nueva…";
    }

    internal void Apply(HotkeyRowViewModel row, HotkeyBinding? binding)
    {
        var target = binding ?? row.Action.Default;
        var message = _binder.Change(row.Id, binding) switch
        {
            ChangeResult.UsedByOther => $"Ya la usa: {_binder.LastConflict?.Name}",
            ChangeResult.Rejected => $"Otro programa ya usa {target.Display}",
            _ => null,
        };
        row.Stop(message, message is not null);
    }

    private void Refresh()
    {
        foreach (var row in Rows.Where(r => !r.Capturing))
            row.Text = _map.Text(row.Id);
    }

    internal string TextOf(string id) => _map.Text(id);
}

public sealed partial class HotkeyRowViewModel(HotkeysViewModel owner, HotkeyAction action) : ObservableObject
{
    public HotkeyAction Action => action;
    public string Id => action.Id;
    public string Name => action.Name;

    [ObservableProperty] public partial string Text { get; set; } = "";
    [ObservableProperty] public partial bool Capturing { get; set; }
    [ObservableProperty] public partial string? Message { get; set; }
    [ObservableProperty] public partial bool IsError { get; set; }

    [RelayCommand]
    private void StartCapture() => owner.StartCapture(this);

    [RelayCommand]
    private void Reset() => owner.Apply(this, null);

    internal void Stop(string? message, bool error)
    {
        Capturing = false;
        Message = message;
        IsError = error;
        Text = owner.TextOf(Id);
    }
}
