using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BoCoach.Core.Engine;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Regions;

namespace BoCoach.App.Overlay;

/// <summary>
/// La segunda parte del overlay: la GD, un número por recurso del HUD con los aldeanos que tendría que haber ahí
/// (verde si coincide, rojo si faltan, amarillo si sobran). Con el mod va chico dentro de la franja de color del
/// cuadrado de cada recurso (así no tapa la cola de producción del juego, que aparece debajo del panel); sin el mod,
/// grande debajo de cada caja. Sigue a las cajas calibradas y a la ventana del juego. Click-through.
/// </summary>
public sealed class ResourceOverlay : Window
{
    /// <summary>Separación entre el borde de abajo de la caja del recurso y el número, en píxeles físicos.</summary>
    public const int Gap = 4;

    /// <summary>Alto reservado para los números debajo de las cajas, en píxeles físicos.</summary>
    public const int BandHeight = 56;

    /// <summary>Ancho fijo de cada número (en unidades de WPF), así no se corre cuando cambia de una a dos cifras.</summary>
    public const double BoxWidth = 64;

    public static readonly Brush OkBrush = Frozen(0x6F, 0xD8, 0x6F);
    public static readonly Brush ShortBrush = Frozen(0xF0, 0x5A, 0x50);
    public static readonly Brush OverBrush = Frozen(0xF2, 0xD0, 0x4A);
    public static readonly Brush UnknownBrush = Frozen(0xF4, 0xF2, 0xEE);

    private readonly RegionLayout _layout;
    private readonly Canvas _canvas = new();
    private readonly Dictionary<ResourceTargetViewModel, (Border Box, TextBlock Text)> _items = [];

    public ResourceOverlay(IReadOnlyList<ResourceTargetViewModel> targets, RegionLayout layout)
    {
        _layout = layout;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        ResizeMode = ResizeMode.NoResize;
        Content = _canvas;
        foreach (var target in targets)
        {
            var text = new TextBlock
            {
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
            };
            var box = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x1E, 0x1A, 0x16)),
                CornerRadius = new CornerRadius(5),
                Width = BoxWidth,
                Padding = new Thickness(8, 0, 8, 2),
                Child = text,
            };
            _canvas.Children.Add(box);
            _items[target] = (box, text);
            target.PropertyChanged += OnTargetChanged;
            Refresh(target);
        }
        SourceInitialized += (_, _) => OverlayWindows.MakeClickThrough(this);
        Closed += (_, _) =>
        {
            foreach (var target in _items.Keys)
                target.PropertyChanged -= OnTargetChanged;
        };
    }

    public static Brush BrushFor(TargetMatch? match) => match switch
    {
        TargetMatch.Ok => OkBrush,
        TargetMatch.Short => ShortBrush,
        TargetMatch.Over => OverBrush,
        _ => UnknownBrush,
    };

    /// <summary>Filas de abajo de la franja que la GD deja libres: el lector las usa para reconocer el cuadrado del mod.</summary>
    public const int BandMargin = 4;

    private PixelRect? _area;

    /// <summary>
    /// Ocupa el ancho del juego desde arriba hasta la franja de debajo de la barra de recursos. Con el mod, cada número
    /// va chico dentro de la franja de color de su cuadrado; sin el mod, centrado debajo de su caja.
    /// </summary>
    public void PlaceOver(PixelRect area)
    {
        _area = area;
        var scale = Scale();
        var boxes = Boxes(area);
        var bottom = boxes.Count > 0 ? boxes.Values.Max(b => b.Y + b.Height) + Gap + BandHeight : area.Y + BandHeight;
        Width = area.Width / scale;
        Height = (bottom - area.Y) / scale;
        OverlayWindows.MoveTo(this, area.X, area.Y);
        foreach (var target in _items.Keys)
            Position(target);
    }

    private double Scale() => PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;

    private Dictionary<ResourceTargetViewModel, PixelRect> Boxes(PixelRect area) =>
        _items.Keys.Where(t => _layout.Regions.ContainsKey(t.Region)).ToDictionary(t => t, t => _layout.PixelsOf(t.Region, area));

    private void Position(ResourceTargetViewModel target)
    {
        var (box, text) = _items[target];
        if (_area is not { } area || !_layout.Regions.ContainsKey(target.Region))
        {
            box.Visibility = Visibility.Collapsed;
            return;
        }
        var scale = Scale();
        var rect = _layout.PixelsOf(target.Region, area);
        if (target.Band is { } band)
        {
            // Dentro de la franja, sin tocar sus últimas filas ni sus costados (el lector mide la franja por ahí).
            var height = Math.Max(8, band.Height - BandMargin);
            box.Width = band.Width * 0.7 / scale;
            box.Height = height / scale;
            box.Padding = new Thickness(0);
            box.CornerRadius = new CornerRadius(3);
            text.FontSize = Math.Max(7, height * 0.8) / scale;
            text.VerticalAlignment = VerticalAlignment.Center;
            Canvas.SetLeft(box, (rect.X - area.X + band.X + band.Width * 0.15) / scale);
            Canvas.SetTop(box, (rect.Y - area.Y + band.Y) / scale);
        }
        else
        {
            var below = Boxes(area).Values.Max(b => b.Y + b.Height) + Gap;
            box.Width = BoxWidth;
            box.Height = double.NaN;
            box.Padding = new Thickness(8, 0, 8, 2);
            box.CornerRadius = new CornerRadius(5);
            text.FontSize = 26;
            Canvas.SetLeft(box, (rect.X - area.X + rect.Width / 2.0) / scale - BoxWidth / 2);
            Canvas.SetTop(box, (below - area.Y) / scale);
        }
    }

    private void OnTargetChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ResourceTargetViewModel target)
            return;
        if (e.PropertyName == nameof(ResourceTargetViewModel.Band))
            Position(target);
        Refresh(target);
    }

    private void Refresh(ResourceTargetViewModel target)
    {
        var (box, text) = _items[target];
        text.Text = target.Target is null ? "" : target.Target + (target.Estimated ? "~" : "");
        text.Foreground = BrushFor(target.Match);
        box.Visibility = target.Target is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
