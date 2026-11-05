using System.Windows;
using BoCoach.Vision.Capture;

namespace BoCoach.App.Overlay;

/// <summary>
/// El overlay sobre el juego: paso actual, siguiente, en curso, atrasados, objetivo y sugerencia de aldeanos.
/// No recibe el mouse ni el foco (click-through), así que no molesta para jugar.
/// </summary>
public partial class CoachOverlay : Window
{
    /// <summary>Margen con el borde derecho de la zona del juego, en píxeles físicos.</summary>
    public const int RightMargin = 16;

    /// <summary>Altura desde arriba, como fracción del alto del juego: debajo de la barra de recursos y los botones.</summary>
    public const double TopFraction = 0.09;

    public CoachOverlay(OverlayViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += (_, _) => OverlayWindows.MakeClickThrough(this);
        // El clic de edad del rival titila (pedido del usuario: "mega molesto").
        var blink = new System.Windows.Threading.DispatcherTimer { Interval = OverlayViewModel.BlinkInterval };
        blink.Tick += (_, _) => viewModel.Blink();
        blink.Start();
        Closed += (_, _) => blink.Stop();
        // Con monitores de distinto DPI, la cuenta de abajo se hizo con la escala del monitor de donde venía la ventana.
        DpiChanged += (_, _) =>
        {
            if (_area is { } area)
                PlaceOver(area);
        };
    }

    private PixelRect? _area;

    /// <summary>
    /// Lo ubica arriba a la derecha de la zona del juego (lejos del log de eventos, que está a la izquierda), siempre en
    /// el mismo lugar: también en la pantalla de carga.
    /// </summary>
    public void PlaceOver(PixelRect area)
    {
        _area = area;
        var scale = VisualTreeHelperScale();
        var width = (int)Math.Ceiling(Width * scale);
        OverlayWindows.MoveTo(this, area.X + area.Width - width - RightMargin, area.Y + (int)(area.Height * TopFraction));
    }

    /// <summary>Píxeles físicos por unidad de WPF en el monitor de la ventana (1 a 100 %, 1,25 a 125 %...).</summary>
    private double VisualTreeHelperScale() =>
        PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
}
