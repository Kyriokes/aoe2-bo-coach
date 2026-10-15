using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BoCoach.Vision.Capture;

/// <summary>
/// Dónde está el juego en la pantalla: el área de cliente de la ventana de AoE2:DE, en píxeles físicos.
/// Solo usa la posición de la ventana (APIs públicas de Windows), nunca la memoria del proceso.
/// </summary>
public static class GameWindow
{
    /// <summary>Nombres del proceso del juego: Steam y Microsoft Store.</summary>
    public static IReadOnlyList<string> ProcessNames { get; } = ["AoE2DE_s", "AoE2DE"];

    /// <summary>Pantalla principal completa (si el juego no está abierto se calibra sobre ella).</summary>
    public static PixelRect PrimaryScreen =>
        new(0, 0, Gdi.GetSystemMetrics(Gdi.SmCxScreen), Gdi.GetSystemMetrics(Gdi.SmCyScreen));

    /// <summary>Área de cliente del juego, o null si no está abierto o está minimizado.</summary>
    public static PixelRect? FindClientArea()
    {
        foreach (var name in ProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    var handle = process.MainWindowHandle;
                    if (handle == IntPtr.Zero || IsIconic(handle) || !GetClientRect(handle, out var client))
                        continue;
                    var origin = new Point();
                    if (!ClientToScreen(handle, ref origin) || client.Right <= 0 || client.Bottom <= 0)
                        continue;
                    return new PixelRect(origin.X, origin.Y, client.Right, client.Bottom);
                }
            }
        }
        return null;
    }

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
}
