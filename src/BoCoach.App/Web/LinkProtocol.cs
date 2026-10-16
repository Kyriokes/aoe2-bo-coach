using Microsoft.Win32;

namespace BoCoach.App.Web;

/// <summary>
/// Los enlaces bocoach://import/&lt;código&gt; de la web ("Abrir en BO Coach"): Windows los manda a esta app si el
/// protocolo está registrado para el usuario (HKCU\Software\Classes\bocoach, sin permisos de administrador). Se
/// registra recién cuando el usuario lo pide desde "Cuenta de la web".
/// </summary>
public sealed class LinkProtocol(string classesKey = @"Software\Classes")
{
    public const string Scheme = "bocoach";

    private string Key => $@"{classesKey}\{Scheme}";

    /// <summary>Comando que Windows corre al abrir un enlace: esta app con --import "bocoach://...".</summary>
    public static string CommandFor(string exePath) => $"\"{exePath}\" --import \"%1\"";

    /// <summary>True si los enlaces abren este mismo ejecutable.</summary>
    public bool IsRegisteredTo(string exePath)
    {
        using var command = Registry.CurrentUser.OpenSubKey($@"{Key}\shell\open\command");
        return string.Equals(command?.GetValue(null) as string, CommandFor(exePath), StringComparison.OrdinalIgnoreCase);
    }

    public void Register(string exePath)
    {
        using var root = Registry.CurrentUser.CreateSubKey(Key);
        root.SetValue(null, "URL:AoE2 BO Coach");
        root.SetValue("URL Protocol", "");
        using (var icon = root.CreateSubKey("DefaultIcon"))
            icon.SetValue(null, $"\"{exePath}\",0");
        using var command = root.CreateSubKey(@"shell\open\command");
        command.SetValue(null, CommandFor(exePath));
    }

    public void Unregister() => Registry.CurrentUser.DeleteSubKeyTree(Key, throwOnMissingSubKey: false);
}
