using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BoCoach.App.Overlay;

/// <summary>
/// Recordatorios que siguen al mouse, abajo a la derecha del cursor: una casa cuando quedan 3 lugares de población, un
/// centro urbano cuando está parado y el botón de inactivos cuando hay aldeanos sin hacer nada. Con los íconos del
/// juego (<see cref="GameIcons"/>) si se encuentra la instalación; si no, dibujos propios. Pegados al mouse es
/// imposible no verlos; por eso son chicos. Click-through.
/// </summary>
public sealed class CursorReminder : Window
{
    /// <summary>Distancia al cursor, en píxeles físicos (que no tape la punta del puntero del juego).</summary>
    public const int OffsetX = 20;
    public const int OffsetY = 24;

    private readonly OverlayViewModel _overlay;
    private readonly FrameworkElement _house;
    private readonly FrameworkElement _townCenter;
    private readonly FrameworkElement _idle;
    private bool _hidden;
    private POINT _last = new() { X = int.MinValue };

    public CursorReminder(OverlayViewModel overlay, GameIcons? icons = null)
    {
        icons ??= GameIcons.Current;
        _overlay = overlay;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;

        // Casa: techo a dos aguas y puerta. Centro urbano: muralla almenada con bandera.
        // Sin el juego: casa con techo a dos aguas y puerta; centro urbano, muralla almenada con bandera; inactivo, un
        // aldeano con un "z".
        var house = Color.FromRgb(0xE8, 0x95, 0x3C);
        var townCenter = Color.FromRgb(0xF2, 0xD0, 0x4A);
        var idle = Color.FromRgb(0xE0, 0x3C, 0x31);
        _house = icons.House is { } houseIcon ? Badge(houseIcon, house)
            : Badge("M10,1 L19,9 L16,9 L16,19 L4,19 L4,9 L1,9 Z", house, "M8.5,19 V13.5 H11.5 V19 Z");
        _townCenter = icons.TownCenter is { } townCenterIcon ? Badge(townCenterIcon, townCenter)
            : Badge("M1,19 V9 H4 V6 H7 V9 H13 V6 H16 V9 H19 V19 Z", townCenter, "M8.5,19 V14 H11.5 V19 Z M10,9 V1 L15,2.8 L10,4.6 Z");
        _idle = icons.IdleVillager is { } idleIcon ? Badge(idleIcon, idle)
            : Badge("M7,5 A2.5,2.5 0 1 1 7,5.01 Z M4,19 L5,9 H9 L10,19 Z", idle, "M12,2 H17 L12,7 H17");
        Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { _house, _townCenter, _idle } };

        // Solo mientras se ve: unas 60 veces por segundo, y mueve la ventana solo si el mouse se movió.
        _follow = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
        _follow.Tick += Follow;
        SourceInitialized += (_, _) => OverlayWindows.MakeClickThrough(this);
        _overlay.PropertyChanged += OnOverlayChanged;
        Closed += (_, _) =>
        {
            _overlay.PropertyChanged -= OnOverlayChanged;
            _follow.Stop();
        };
        Refresh();
    }

    private readonly System.Windows.Threading.DispatcherTimer _follow;

    /// <summary>Lo oculta junto con el resto del overlay (Ctrl+Alt+H), sin apagar los recordatorios.</summary>
    public void SetHidden(bool hidden)
    {
        _hidden = hidden;
        Refresh();
    }

    private static Border Badge(string shape, Color fill, string detail)
    {
        var outline = new SolidColorBrush(Color.FromRgb(0x1E, 0x1A, 0x16));
        var canvas = new Canvas { Width = 20, Height = 20 };
        canvas.Children.Add(new Path { Data = Geometry.Parse(shape), Fill = new SolidColorBrush(fill), Stroke = outline, StrokeThickness = 1.2 });
        canvas.Children.Add(new Path { Data = Geometry.Parse(detail), Fill = outline, Stroke = outline, StrokeThickness = 0.8 });
        return Frame(canvas, fill, padding: 3);
    }

    /// <summary>Un ícono del juego, del mismo tamaño que los dibujos (26 px con el marco).</summary>
    private static Border Badge(ImageSource icon, Color fill)
    {
        var image = new Image { Source = icon, Width = 24, Height = 24, Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return Frame(image, fill, padding: 0);
    }

    private static Border Frame(UIElement child, Color fill, double padding)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x1E, 0x1A, 0x16)),
            BorderBrush = new SolidColorBrush(fill),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(padding),
            Margin = new Thickness(0, 0, 3, 0),
            Child = child,
        };
    }

    private void OnOverlayChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OverlayViewModel.ShowCursorReminder) or nameof(OverlayViewModel.HouseReminder)
            or nameof(OverlayViewModel.TownCenterReminder) or nameof(OverlayViewModel.IdleReminder) or nameof(OverlayViewModel.CursorReminders))
            Refresh();
    }

    private void Refresh()
    {
        _house.Visibility = _overlay.HouseReminder ? Visibility.Visible : Visibility.Collapsed;
        _townCenter.Visibility = _overlay.TownCenterReminder ? Visibility.Visible : Visibility.Collapsed;
        _idle.Visibility = _overlay.IdleReminder ? Visibility.Visible : Visibility.Collapsed;
        if (_overlay.ShowCursorReminder && !_hidden)
        {
            if (!IsVisible)
            {
                _last = new POINT { X = int.MinValue };
                Follow(null, EventArgs.Empty);
                Show();
                _follow.Start();
            }
        }
        else if (IsVisible)
        {
            _follow.Stop();
            Hide();
        }
    }

    /// <summary>Si el mouse se movió, lo sigue.</summary>
    private void Follow(object? sender, EventArgs e)
    {
        if (!GetCursorPos(out var cursor) || (cursor.X == _last.X && cursor.Y == _last.Y))
            return;
        _last = cursor;
        OverlayWindows.MoveTo(this, cursor.X + OffsetX, cursor.Y + OffsetY);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);
}
