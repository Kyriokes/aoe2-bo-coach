using System.Text.RegularExpressions;
using BoCoach.Core.Web;

namespace BoCoach.App.Web;

/// <summary>
/// Lo que llega por un enlace bocoach:// (de la web) o por "Pegar código": conectar la cuenta con un token
/// (<c>bocoach://conectar/&lt;token&gt;</c>, desde "Conectar la app" de Mi cuenta) o sumar una BO por su código.
/// </summary>
public abstract partial record AppLink
{
    public sealed record Connect(string Token) : AppLink;

    public sealed record Import(string Code) : AppLink;

    [GeneratedRegex(@"^bocoach://conectar/(bocoach_[0-9a-f]{64})/?$", RegexOptions.IgnoreCase)]
    private static partial Regex ConnectPattern();

    /// <summary>El enlace, o null si no es ninguno de los dos (un token mal formado tampoco).</summary>
    public static AppLink? Parse(string? text)
    {
        var trimmed = (text ?? "").Trim();
        if (ConnectPattern().Match(trimmed) is { Success: true } connect)
            return new Connect(connect.Groups[1].Value.ToLowerInvariant());
        return WebCode.Parse(trimmed) is { } code ? new Import(code) : null;
    }
}
