using System.Windows.Input;

namespace BoCoach.App.Hotkeys;

/// <summary>
/// Una combinación de atajo global: modificadores y una tecla. Se guarda en settings.json como texto con los nombres
/// de <see cref="Key"/> ("Ctrl+Alt+Right") y se muestra con flechas ("Ctrl+Alt+→").
/// </summary>
public readonly record struct HotkeyBinding(ModifierKeys Modifiers, Key Key)
{
    /// <summary>
    /// Lleva Ctrl, Alt o Win y la tecla no es un modificador. Una tecla sola (o con Shift) no sirve: RegisterHotKey la
    /// captura en todo Windows y se la robaría al juego.
    /// </summary>
    public bool IsValid =>
        (Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0 && !IsModifier(Key) && Key != Key.None;

    /// <summary>Texto para settings.json, con los modificadores en orden fijo: Ctrl, Alt, Shift, Win.</summary>
    public string Storage => Join(Key.ToString());

    /// <summary>Texto para mostrar: igual que <see cref="Storage"/> pero con las flechas como símbolos.</summary>
    public string Display => Join(Key switch
    {
        Key.Right => "→",
        Key.Left => "←",
        Key.Up => "↑",
        Key.Down => "↓",
        _ => Key.ToString(),
    });

    /// <summary>Lee "Ctrl+Alt+Right" (sin importar el orden ni las mayúsculas); null si no se entiende o no es válida.</summary>
    public static HotkeyBinding? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var modifiers = ModifierKeys.None;
        Key? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= ModifierKeys.Control; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                case "shift": modifiers |= ModifierKeys.Shift; break;
                case "win" or "windows": modifiers |= ModifierKeys.Windows; break;
                default:
                    if (key is not null || int.TryParse(part, out _) || !Enum.TryParse<Key>(part, ignoreCase: true, out var parsed))
                        return null;
                    key = parsed;
                    break;
            }
        }
        if (key is not { } found)
            return null;
        var binding = new HotkeyBinding(modifiers, found);
        return binding.IsValid ? binding : null;
    }

    public static bool IsModifier(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;

    private string Join(string key)
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key);
        return string.Join('+', parts);
    }
}
