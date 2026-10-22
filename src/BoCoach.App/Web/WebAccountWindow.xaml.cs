using System.Diagnostics;
using System.Windows;
using BoCoach.Core.Web;

namespace BoCoach.App.Web;

/// <summary>Dirección de la web, token de la app y registro de los enlaces bocoach://.</summary>
public partial class WebAccountWindow : Window
{
    private readonly WebSettings _current;
    private readonly LinkProtocol _links = new();

    public WebAccountWindow(WebSettings current)
    {
        InitializeComponent();
        _current = current;
        UrlBox.Text = current.WebUrl;
        TokenBox.Password = current.Token;
        UpdateLinkStatus();
    }

    /// <summary>Lo que se guardó, o null si se canceló.</summary>
    public WebSettings? Result { get; private set; }

    private static string ExePath => Environment.ProcessPath ?? "";

    private void UpdateLinkStatus()
    {
        var registered = ExePath.Length > 0 && _links.IsRegisteredTo(ExePath);
        LinkStatus.Text = registered ? "Registrados para esta app." : "Sin registrar.";
        RegisterButton.IsEnabled = !registered;
    }

    private void RegisterLinks(object sender, RoutedEventArgs e)
    {
        _links.Register(ExePath);
        UpdateLinkStatus();
    }

    private void OpenAccountPage(object sender, RoutedEventArgs e)
    {
        if (Uri.TryCreate(UrlBox.Text.Trim(), UriKind.Absolute, out var url) && url.Scheme is "https" or "http")
            Process.Start(new ProcessStartInfo(new Uri(url, "cuenta").ToString()) { UseShellExecute = true });
    }

    private void Save(object sender, RoutedEventArgs e)
    {
        var url = UrlBox.Text.Trim();
        // http solo para la web corriendo en esta PC (desarrollo): por la red, el token viajaría sin cifrar.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !WebLibraryClient.SendsTokenSafely(uri))
        {
            MessageBox.Show(this, "La dirección de la web tiene que empezar con https://", "Cuenta de la web",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var token = TokenBox.Password.Trim();
        if (token.Length > 0 && !token.StartsWith("bocoach_", StringComparison.Ordinal))
        {
            MessageBox.Show(this, "El token de la app empieza con bocoach_. Copialo de Mi cuenta en la web.", "Cuenta de la web",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Result = _current with { WebUrl = url.TrimEnd('/'), Token = token };
        DialogResult = true;
    }
}
