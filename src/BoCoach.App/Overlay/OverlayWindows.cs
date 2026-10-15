using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using BoCoach.Vision.Capture;

namespace BoCoach.App.Overlay;

/// <summary>Utilidades Win32 para ventanas que se dibujan sobre el juego.</summary>
public static class OverlayWindows
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExToolWindow = 0x80;
    private const int WsExLayered = 0x80000;
    private const int WsExNoActivate = 0x8000000;
    private const uint SwpNoSize = 0x1;
    private const uint SwpNoActivate = 0x10;
    private static readonly IntPtr HwndTopmost = new(-1);

    /// <summary>La ventana deja pasar el mouse, no roba el foco y no aparece en Alt+Tab.</summary>
    public static void MakeClickThrough(Window window)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, style | WsExTransparent | WsExToolWindow | WsExLayered | WsExNoActivate);
    }

    /// <summary>Mueve la ventana a una posición en píxeles físicos, encima de todo y sin activarla.</summary>
    public static void MoveTo(Window window, int x, int y)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        SetWindowPos(handle, HwndTopmost, x, y, 0, 0, SwpNoSize | SwpNoActivate);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}

/// <summary>Cartelito "● Grabando 3:21" arriba del juego, que no molesta al mouse ni al teclado.</summary>
public sealed class RecordingIndicator : Window
{
    private readonly TextBlock _text = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0x80, 0x70)),
        FontFamily = new FontFamily("Segoe UI"),
        FontSize = 13,
        FontWeight = FontWeights.SemiBold,
    };

    public RecordingIndicator()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x1E, 0x1A, 0x16)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 4, 10, 4),
            Child = _text,
        };
        SourceInitialized += (_, _) => OverlayWindows.MakeClickThrough(this);
    }

    /// <summary>Lo muestra arriba al centro de la zona del juego (un poco debajo de la barra de recursos).</summary>
    public void ShowOver(PixelRect area, string text)
    {
        _text.Text = text;
        Show();
        OverlayWindows.MoveTo(this, area.X + area.Width / 2 - 70, area.Y + area.Height / 16);
    }

    public void SetText(string text) => _text.Text = text;
}
