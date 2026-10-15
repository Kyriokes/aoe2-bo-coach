using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace BoCoach.App.Overlay;

/// <summary>
/// Atajos de teclado que funcionan aunque el foco esté en el juego (RegisterHotKey de Win32).
/// No envían nada al juego: solo avisan a la app.
/// </summary>
public sealed class GlobalHotkeys : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;

    private readonly IntPtr _handle;
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = [];
    private int _nextId = 0x0B00;

    public GlobalHotkeys(Window window)
    {
        _handle = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(_handle);
        _source.AddHook(Hook);
    }

    /// <summary>Registra el atajo. Devuelve false si otro programa ya lo usa.</summary>
    public bool Register(ModifierKeys modifiers, Key key, Action action)
    {
        var id = _nextId++;
        var mods = ModNoRepeat
            | (modifiers.HasFlag(ModifierKeys.Alt) ? 1u : 0)
            | (modifiers.HasFlag(ModifierKeys.Control) ? 2u : 0)
            | (modifiers.HasFlag(ModifierKeys.Shift) ? 4u : 0)
            | (modifiers.HasFlag(ModifierKeys.Windows) ? 8u : 0);
        if (!RegisterHotKey(_handle, id, mods, (uint)KeyInterop.VirtualKeyFromKey(key)))
            return false;
        _actions[id] = action;
        return true;
    }

    public void Dispose()
    {
        foreach (var id in _actions.Keys)
            UnregisterHotKey(_handle, id);
        _actions.Clear();
        _source.RemoveHook(Hook);
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            action();
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
