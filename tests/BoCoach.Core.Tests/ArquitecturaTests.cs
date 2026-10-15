using System.Reflection;
using System.Runtime.Versioning;

namespace BoCoach.Core.Tests;

// Core es lógica pura: no puede depender de Windows, WPF ni de los otros proyectos de la app.
public class ArquitecturaTests
{
    private static readonly string[] EnsambladosProhibidos =
    [
        "WindowsBase",
        "PresentationCore",
        "PresentationFramework",
        "System.Windows.Forms",
        "System.Drawing.Common",
        "Microsoft.Windows.SDK.NET",
        "WinRT.Runtime",
        "BoCoach.Vision",
        "BoCoach.App",
    ];

    private static readonly Assembly Core = Assembly.Load("BoCoach.Core");

    [Fact]
    public void Core_no_referencia_ensamblados_de_Windows_ni_de_la_app()
    {
        var referencias = Core.GetReferencedAssemblies().Select(a => a.Name);

        Assert.Empty(referencias.Intersect(EnsambladosProhibidos));
    }

    [Fact]
    public void Core_no_esta_atado_a_un_sistema_operativo()
    {
        Assert.Null(Core.GetCustomAttribute<SupportedOSPlatformAttribute>());
    }
}
