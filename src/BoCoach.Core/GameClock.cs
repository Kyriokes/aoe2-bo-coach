using System.Globalization;

namespace BoCoach.Core;

/// <summary>Tiempo de partida como lo muestra el juego: "9:00", "12:05", "1:02:30".</summary>
public static class GameClock
{
    public static string Format(double seconds)
    {
        var s = (int)Math.Round(Math.Max(0, seconds));
        return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
    }

    /// <summary>Acepta "9:00", "9:5", "1:02:30" o segundos sueltos ("540").</summary>
    public static bool TryParse(string? text, out int seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Trim().Split(':');
        if (parts.Length > 3)
            return false;
        var total = 0;
        foreach (var part in parts)
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                return false;
            total = total * 60 + n;
        }
        seconds = total;
        return true;
    }
}
