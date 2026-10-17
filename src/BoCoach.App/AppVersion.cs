using System.Globalization;
using System.Reflection;

namespace BoCoach.App;

/// <summary>
/// La versión que se muestra en la app: número, commit de git y cuándo se compiló, por ejemplo
/// "v0.9.1 · b046d49 · 17/10 02:45". El commit lo agrega el SDK al compilar (el HEAD de ese momento).
/// </summary>
public static class AppVersion
{
    public static string Text { get; } = Describe(typeof(AppVersion).Assembly);

    public static string WindowTitle => $"AoE2 BO Coach {Text} · Editor de build orders";

    private static string Describe(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
        var parts = informational.Split('+');
        var text = $"v{parts[0]}";
        if (parts.Length > 1)
            text += $" · {parts[1][..Math.Min(7, parts[1].Length)]}";
        var built = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "BuildTime")?.Value;
        if (DateTime.TryParseExact(built, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            text += $" · {time:dd/MM HH:mm}";
        return text;
    }
}
