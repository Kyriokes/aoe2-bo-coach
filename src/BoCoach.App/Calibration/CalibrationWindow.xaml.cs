using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Ocr;
using BoCoach.Vision.Regions;

namespace BoCoach.App.Calibration;

/// <summary>
/// Ventana transparente sobre el juego con un rectángulo por región. Se arrastran y se estiran con el mouse
/// (o con las flechas) y debajo de cada uno se muestra en vivo lo que lee el OCR.
/// El rectángulo de color queda por fuera de la región, así no tapa lo que se captura.
/// </summary>
public partial class CalibrationWindow : Window
{
    private const double Stroke = 2;
    private const double GripSize = 12;

    private static readonly Dictionary<string, Color> RegionColors = new()
    {
        [RegionNames.Wood] = Color.FromRgb(0xC0, 0x8A, 0x4F),
        [RegionNames.Food] = Color.FromRgb(0xE0, 0x5A, 0x47),
        [RegionNames.Gold] = Color.FromRgb(0xE8, 0xC5, 0x47),
        [RegionNames.Stone] = Color.FromRgb(0xA9, 0xB0, 0xB8),
        [RegionNames.Population] = Color.FromRgb(0x6F, 0xB1, 0xE8),
        [RegionNames.Clock] = Color.FromRgb(0x8C, 0xD1, 0x7D),
        [RegionNames.EventLog] = Color.FromRgb(0xC3, 0x8B, 0xE8),
        [RegionNames.IdleVillagers] = Color.FromRgb(0xF0, 0x60, 0x60),
        [RegionNames.Age] = Color.FromRgb(0xF4, 0xF2, 0xEE),
    };

    private static readonly IntPtr HwndTopmost = new(-1);

    private readonly IScreenSource _screen;
    private readonly OcrReader? _ocr;
    private readonly PixelRect _area;
    private readonly Dictionary<string, RegionBox> _boxes = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private string? _selected;
    private bool _reading;

    public CalibrationWindow(RegionLayout layout, IScreenSource screen, OcrReader? ocr)
    {
        InitializeComponent();
        Layout = layout;
        _screen = screen;
        _ocr = ocr;
        _area = screen.CaptureArea;

        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
        SourceInitialized += (_, _) =>
        {
            // Encima de la ventana del juego, en píxeles físicos (sirve en cualquier monitor).
            if (PlaceOverGame)
            {
                var handle = new WindowInteropHelper(this).Handle;
                SetWindowPos(handle, HwndTopmost, _area.X, _area.Y, _area.Width, _area.Height, 0);
            }
        };

        StatusText.Text = (screen.GameFound
                ? $"Juego encontrado: {_area.Width}×{_area.Height} en ({_area.X}, {_area.Y})."
                : "No encontré la ventana del juego: calibro sobre la pantalla principal. Abrí el juego y volvé a calibrar.")
            + (ocr is null ? "\nEl OCR en inglés de Windows no está instalado: podés ubicar los rectángulos igual, pero sin lectura en vivo." : "");

        foreach (var name in RegionNames.All)
            _boxes[name] = CreateBox(name);
        Select(RegionNames.Wood);

        SizeChanged += (_, _) => PositionAll();
        Loaded += (_, _) =>
        {
            PositionAll();
            Activate();
            if (_ocr is not null)
            {
                _timer.Tick += async (_, _) => await ReadAllAsync();
                _timer.Start();
            }
        };
        Closed += (_, _) => _timer.Stop();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>Las regiones como quedaron (con la resolución de referencia al guardar).</summary>
    public RegionLayout Layout { get; private set; }

    /// <summary>Si es false, la ventana queda donde se la ubique (para dibujarla a PNG en el modo captura).</summary>
    public bool PlaceOverGame { get; init; } = true;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    private sealed record RegionBox(Thumb Body, Thumb Grip, TextBlock Label, TextBlock Reading);

    private RegionBox CreateBox(string name)
    {
        var brush = new SolidColorBrush(RegionColors[name]);
        var body = new Thumb
        {
            Template = (ControlTemplate)Resources["BoxTemplate"],
            BorderBrush = brush,
            BorderThickness = new Thickness(Stroke),
            Cursor = Cursors.SizeAll,
            ToolTip = $"{RegionNames.Label(name)}: arrastrá para mover",
        };
        var grip = new Thumb
        {
            Template = (ControlTemplate)Resources["GripTemplate"],
            BorderBrush = brush,
            Width = GripSize,
            Height = GripSize,
            Cursor = Cursors.SizeNWSE,
            ToolTip = "Arrastrá para cambiar el tamaño",
        };
        var label = new TextBlock
        {
            Text = RegionNames.Label(name),
            Background = brush,
            Foreground = Brushes.Black,
            FontWeight = FontWeights.SemiBold,
            FontSize = 11,
            Padding = new Thickness(4, 1, 4, 1),
            IsHitTestVisible = false,
        };
        var reading = new TextBlock
        {
            Background = new SolidColorBrush(Color.FromArgb(0xDD, 0x1E, 0x1A, 0x16)),
            Foreground = brush,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Padding = new Thickness(4, 1, 4, 1),
            MaxWidth = 420,
            TextWrapping = TextWrapping.Wrap,
            IsHitTestVisible = false,
            Text = "…",
            Visibility = _ocr is null ? Visibility.Collapsed : Visibility.Visible,
        };

        body.DragDelta += (_, e) => Change(name, dx: e.HorizontalChange, dy: e.VerticalChange);
        grip.DragDelta += (_, e) => Change(name, dw: e.HorizontalChange, dh: e.VerticalChange);
        body.PreviewMouseLeftButtonDown += (_, _) => Select(name);
        grip.PreviewMouseLeftButtonDown += (_, _) => Select(name);

        Surface.Children.Add(body);
        Surface.Children.Add(grip);
        Surface.Children.Add(label);
        Surface.Children.Add(reading);
        return new RegionBox(body, grip, label, reading);
    }

    /// <summary>Mueve o estira una región; los cambios llegan en píxeles de la ventana y se guardan como fracción.</summary>
    private void Change(string name, double dx = 0, double dy = 0, double dw = 0, double dh = 0)
    {
        var (w, h) = (Surface.ActualWidth, Surface.ActualHeight);
        if (w <= 0 || h <= 0)
            return;
        var r = Layout.Regions[name];
        Layout = Layout.With(name, new RegionRect(r.X + dx / w, r.Y + dy / h, r.Width + dw / w, r.Height + dh / h));
        Position(name);
    }

    private void PositionAll()
    {
        foreach (var name in _boxes.Keys)
            Position(name);
    }

    private void Position(string name)
    {
        var (w, h) = (Surface.ActualWidth, Surface.ActualHeight);
        var r = Layout.Regions[name];
        var box = _boxes[name];

        var left = r.X * w - Stroke;
        var top = r.Y * h - Stroke;
        var width = r.Width * w + 2 * Stroke;
        var height = r.Height * h + 2 * Stroke;

        Place(box.Body, left, top);
        box.Body.Width = width;
        box.Body.Height = height;
        Place(box.Grip, left + width - GripSize / 2, top + height - GripSize / 2);
        const double labelHeight = 18;
        if (top >= labelHeight)
        {
            Place(box.Label, left, top - labelHeight);
            Place(box.Reading, left, top + height + 3);
        }
        else
        {
            // Pegada al borde de arriba (los recursos del HUD): etiqueta y lectura van debajo.
            Place(box.Label, left, top + height + 3);
            Place(box.Reading, left, top + height + 3 + labelHeight);
        }
    }

    private static void Place(UIElement element, double left, double top)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
    }

    private void Select(string name)
    {
        _selected = name;
        foreach (var (key, box) in _boxes)
        {
            box.Body.BorderThickness = new Thickness(key == name ? Stroke + 1 : Stroke);
            Panel.SetZIndex(box.Body, key == name ? 1 : 0);
            Panel.SetZIndex(box.Grip, key == name ? 2 : 1);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_selected is null || e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down))
            return;
        var dx = e.Key switch { Key.Left => -1, Key.Right => 1, _ => 0 };
        var dy = e.Key switch { Key.Up => -1, Key.Down => 1, _ => 0 };
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            Change(_selected, dw: dx, dh: dy);
        else
            Change(_selected, dx: dx, dy: dy);
        e.Handled = true;
    }

    /// <summary>Captura cada región y muestra lo que lee el OCR. Si la lectura anterior no terminó, se saltea.</summary>
    private async Task ReadAllAsync()
    {
        if (_reading || _ocr is null)
            return;
        _reading = true;
        try
        {
            var area = _area;
            foreach (var name in RegionNames.All)
            {
                var image = _screen.Capture(Layout.PixelsOf(name, area));
                var text = (await _ocr.ReadAsync(image)).Trim();
                var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                _boxes[name].Reading.Text = lines.Length == 0
                    ? "(no leo nada)"
                    : string.Join("\n", lines.Take(name == RegionNames.EventLog ? 4 : 1));
            }
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            StatusText.Text = $"No se pudo leer la pantalla: {e.Message}";
        }
        finally
        {
            _reading = false;
        }
    }

    private void PanelHandle_DragDelta(object sender, DragDeltaEventArgs e)
    {
        PanelOffset.X += e.HorizontalChange;
        PanelOffset.Y += e.VerticalChange;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Layout = Layout.WithReference(_area.Width, _area.Height);
        DialogResult = true;
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        Layout = RegionLayout.Default;
        PositionAll();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
