using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BoCoach.Core.Engine;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Regions;

namespace BoCoach.App.Overlay;

/// <summary>
/// La segunda parte del overlay: un número grande debajo de cada recurso del HUD con los aldeanos que tendría que
/// haber ahí (verde si coincide, rojo si faltan, amarillo si sobran). Se ubica con las cajas calibradas de cada
/// recurso, así que sigue al HUD si se calibra de nuevo o se mueve la ventana del juego. Click-through.
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

    /// <summary>Ocupa el ancho del juego y la franja de debajo de la barra de recursos; cada número va centrado bajo su caja.</summary>
    public void PlaceOver(PixelRect area)
    {
        var scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        var boxes = _items.Keys.Where(t => _layout.Regions.ContainsKey(t.Region)).ToDictionary(t => t, t => _layout.PixelsOf(t.Region, area));
        var top = boxes.Count > 0 ? boxes.Values.Max(b => b.Y + b.Height) + Gap : area.Y;
        Width = area.Width / scale;
        Height = BandHeight / scale;
        OverlayWindows.MoveTo(this, area.X, top);
        foreach (var (target, (box, _)) in _items)
        {
            if (!boxes.TryGetValue(target, out var rect))
            {
                box.Visibility = Visibility.Collapsed;
                continue;
            }
            Canvas.SetLeft(box, (rect.X - area.X + rect.Width / 2.0) / scale - BoxWidth / 2);
            Canvas.SetTop(box, 0);
        }
    }

    private void OnTargetChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is ResourceTargetViewModel target)
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
