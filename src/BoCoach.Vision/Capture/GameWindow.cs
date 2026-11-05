using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BoCoach.Vision.Capture;

/// <summary>Un proceso del juego y su ventana principal (cero si no tiene una a la vista: cargando o cerrándose).</summary>
public readonly record struct GameProcess(int Id, IntPtr Window);

/// <summary>Lo que <see cref="GameWindowFinder"/> le pregunta al sistema.</summary>
public interface IGameProcesses
{
    /// <summary>Los procesos del juego. Recorre todos los del sistema: es lo caro.</summary>
    IReadOnlyList<GameProcess> Enumerate();

    /// <summary>Si esa ventana sigue siendo del proceso y sigue a la vista: lo barato.</summary>
    bool StillOpen(GameProcess process);
}

/// <summary>
/// Los procesos del juego sin recorrer los del sistema en cada pregunta: la app pregunta 6 a 8 veces por segundo (cada
/// captura y el estado de la ventana), también sin el juego abierto. Con la ventana en la mano alcanza con validarla;
/// sin ella, se recorren como mucho una vez por segundo.
/// </summary>
/// <param name="now">Segundos de un reloj que no retrocede.</param>
public sealed class GameWindowFinder(IGameProcesses system, Func<double> now)
{
    /// <summary>Sin una ventana que valga, cada cuánto se vuelven a recorrer los procesos.</summary>
    public const double SearchSeconds = 1;

    /// <summary>
    /// Aunque la ventana siga valiendo, cada cuánto se vuelve a mirar cuál es la principal: el juego podría abrir otra sin
    /// cerrar la primera (no se pudo comprobar sin el juego).
    /// </summary>
    public const double RefreshSeconds = 10;

    private readonly object _lock = new();
    private IReadOnlyList<GameProcess> _processes = [];
    private double? _enumeratedAt;

    /// <summary>Los procesos del juego, con la ventana en cero si ya no vale.</summary>
    public IReadOnlyList<GameProcess> Processes()
    {
        lock (_lock)
        {
            var hasWindow = false;
            var lost = false;
            foreach (var process in _processes)
            {
                if (process.Window == IntPtr.Zero)
                    continue;
                if (system.StillOpen(process))
                    hasWindow = true;
                else
                    lost = true;
            }
            var time = now();
            var age = _enumeratedAt is { } at ? time - at : double.MaxValue;
            if (age >= (hasWindow ? RefreshSeconds : SearchSeconds))
            {
                _processes = system.Enumerate();
                _enumeratedAt = time;
            }
            else if (lost)
            {
                _processes = [.. _processes.Select(p => p.Window != IntPtr.Zero && system.StillOpen(p) ? p : p with { Window = IntPtr.Zero })];
            }
            return _processes;
        }
    }
}

/// <summary>
/// Dónde está el juego en la pantalla: el área de cliente de la ventana de AoE2:DE, en píxeles físicos.
/// Solo usa la posición de la ventana (APIs públicas de Windows), nunca la memoria del proceso.
/// </summary>
public static class GameWindow
{
    /// <summary>Nombres del proceso del juego: Steam y Microsoft Store.</summary>
    public static IReadOnlyList<string> ProcessNames { get; } = ["AoE2DE_s", "AoE2DE"];

    private static readonly GameWindowFinder Finder = new(new SystemProcesses(), () => Environment.TickCount64 / 1000.0);

    /// <summary>Pantalla principal completa (si el juego no está abierto se calibra sobre ella).</summary>
    public static PixelRect PrimaryScreen =>
        new(0, 0, Gdi.GetSystemMetrics(Gdi.SmCxScreen), Gdi.GetSystemMetrics(Gdi.SmCyScreen));

    /// <summary>Área de cliente del juego, o null si no está abierto o está minimizado.</summary>
    public static PixelRect? FindClientArea()
    {
        foreach (var process in Finder.Processes())
        {
            var handle = process.Window;
            if (handle == IntPtr.Zero || IsIconic(handle) || !GetClientRect(handle, out var client))
                continue;
            var origin = new Point();
            if (!ClientToScreen(handle, ref origin) || client.Right <= 0 || client.Bottom <= 0)
                continue;
            return new PixelRect(origin.X, origin.Y, client.Right, client.Bottom);
        }
        return null;
    }

    /// <summary>
    /// Si la ventana del juego está al frente: no minimizada, y la ventana activa es la del juego o una de esta app
    /// (tocar "Atrás" o "Ya está" en la ventana no tiene que esconder el coach). null si el juego no está abierto.
    /// </summary>
    public static bool? IsInFront()
    {
        var found = false;
        var foreground = GetForegroundWindow();
        foreach (var process in Finder.Processes())
        {
            var handle = process.Window;
            if (handle == IntPtr.Zero)
                continue;
            found = true;
            if (!IsIconic(handle) && (handle == foreground || GetAncestor(foreground, GaRootOwner) == handle || IsOurs(foreground)))
                return true;
        }
        return found ? false : null;
    }

    /// <summary>Si el proceso del juego está corriendo (solo mira la lista de procesos, nunca su memoria).</summary>
    public static bool IsRunning() => Finder.Processes().Count > 0;

    private static bool IsOurs(IntPtr window)
    {
        if (window == IntPtr.Zero)
            return false;
        GetWindowThreadProcessId(window, out var pid);
        return pid == Environment.ProcessId;
    }

    private sealed class SystemProcesses : IGameProcesses
    {
        public IReadOnlyList<GameProcess> Enumerate()
        {
            var found = new List<GameProcess>();
            foreach (var name in ProcessNames)
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    using (process)
                    {
                        try
                        {
                            found.Add(new GameProcess(process.Id, process.MainWindowHandle));
                        }
                        catch (InvalidOperationException)
                        {
                            // Se cerró entre la lista y la pregunta.
                        }
                    }
                }
            }
            return found;
        }

        public bool StillOpen(GameProcess process) =>
            IsWindow(process.Window) && IsWindowVisible(process.Window)
            && GetWindowThreadProcessId(process.Window, out var pid) != 0 && pid == process.Id;
    }

    private const uint GaRootOwner = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr hWnd, ref Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
