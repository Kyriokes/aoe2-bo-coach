using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BoCoach.App.Coaching;

/// <summary>Ajustes del coach que elige el jugador, guardados en %APPDATA%\BoCoach\settings.json.</summary>
public sealed class CoachSettings
{
    public const int DefaultMaxVillagers = 120;
    public const int MinVillagers = 20;
    public const int MaxVillagersLimit = 200;

    public static string DefaultPath => Path.Combine(CoachData.UserDir, "settings.json");

    /// <summary>Hasta cuántos aldeanos producen sin parar los centros urbanos.</summary>
    public int MaxVillagers { get; set; } = DefaultMaxVillagers;

    /// <summary>Recordatorios junto al mouse (casa, centro urbano parado).</summary>
    public bool CursorReminders { get; set; } = true;

    /// <summary>Civ con la que se juega (clave de game_data); vacío = la de la BO.</summary>
    public string GameCiv { get; set; } = "";

    /// <summary>Idioma en que está el juego (<see cref="Vision.Ocr.GameLanguage.Code"/>): "en", "es" o "mx".</summary>
    public string GameLanguage { get; set; } = "en";

    public static int Clamp(int value) => Math.Clamp(value, MinVillagers, MaxVillagersLimit);

    public static CoachSettings Load(string path)
    {
        try
        {
            if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject json)
            {
                return new CoachSettings
                {
                    MaxVillagers = json["max_villagers"]?.GetValue<int>() is { } max ? Clamp(max) : DefaultMaxVillagers,
                    CursorReminders = json["cursor_reminders"]?.GetValue<bool>() ?? true,
                    GameCiv = json["civ"]?.GetValue<string>()?.Trim().ToLowerInvariant() ?? "",
                    GameLanguage = Vision.Ocr.GameLanguage.FromCode(json["game_language"]?.GetValue<string>()).Code,
                };
            }
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidOperationException or FormatException)
        {
            // Un archivo roto no tiene que impedir abrir la app: quedan los valores por defecto.
        }
        return new CoachSettings();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = new JsonObject { ["max_villagers"] = MaxVillagers, ["cursor_reminders"] = CursorReminders, ["civ"] = GameCiv, ["game_language"] = GameLanguage };
        File.WriteAllText(path, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
}
