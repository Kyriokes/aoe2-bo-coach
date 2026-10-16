using System.Globalization;

namespace BoCoach.Core.Engine;

/// <summary>Una fila de lecturas.csv: la captura, cuándo se tomó y lo leído ya suavizado.</summary>
public sealed record LoggedReading(int Frame, long TimeMs, ScreenReading Reading);

/// <summary>
/// Lecturas de pantalla de una partida en CSV (las escribe ReplaySession replay). Sirve para correr el motor
/// sobre una partida real sin volver a pasar el OCR: frame,time_ms,clock,food,wood,gold,stone,villagers,idle,age,events.
/// </summary>
public static class ScreenReadingLog
{
    public const string Header = "frame,time_ms,clock,food,wood,gold,stone,villagers,idle,age,events";

    public static string Format(int frame, long timeMs, ScreenReading reading, int? idle = null, string? age = null) =>
        string.Join(",",
            frame.ToString(CultureInfo.InvariantCulture),
            timeMs.ToString(CultureInfo.InvariantCulture),
            reading.Clock is { } c ? GameClock.Format(c) : "",
            Number(reading.Food), Number(reading.Wood), Number(reading.Gold), Number(reading.Stone),
            Number(reading.Villagers), Number(idle), age ?? "",
            string.Join(" ", reading.NewEvents));

    public static IReadOnlyList<LoggedReading> Load(string path) => Parse(File.ReadLines(path));

    public static IReadOnlyList<LoggedReading> Parse(IEnumerable<string> lines) => lines
        .Skip(1)
        .Where(line => line.Length > 0)
        .Select(line =>
        {
            var p = line.Split(',');
            double? clock = GameClock.TryParse(p[2], out var seconds) ? seconds : null;
            var reading = new ScreenReading(
                clock, Value(p[3]), Value(p[4]), Value(p[5]), Value(p[6]), (int?)Value(p[7]),
                Population: null, PopulationCap: null,
                p[10].Split(' ', StringSplitOptions.RemoveEmptyEntries));
            return new LoggedReading(int.Parse(p[0], CultureInfo.InvariantCulture), long.Parse(p[1], CultureInfo.InvariantCulture), reading);
        })
        .ToList();

    private static string Number(double? value) => value is { } v ? v.ToString("0", CultureInfo.InvariantCulture) : "";

    private static double? Value(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
}
