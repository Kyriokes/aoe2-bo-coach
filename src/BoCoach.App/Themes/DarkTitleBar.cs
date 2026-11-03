using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BoCoach.App.Themes;

/// <summary>
/// La barra de título de Windows en oscuro, para todas las ventanas de la app. Es un atributo de la ventana (DWM): en
/// una versión de Windows que no lo conoce no pasa nada y queda la barra clara.
/// </summary>
public static class DarkTitleBar
{
    private const int ImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    /// <summary>Se aplica sola a cada ventana que se abre, también a las que se arman por código.</summary>
    public static void ApplyToAllWindows() =>
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is Window { WindowStyle: not WindowStyle.None } window)
                Apply(window);
        }));

    private static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;
        var on = 1;
        _ = DwmSetWindowAttribute(handle, ImmersiveDarkMode, ref on, sizeof(int));
    }
}
