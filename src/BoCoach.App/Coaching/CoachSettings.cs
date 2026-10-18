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
        var json = new JsonObject { ["max_villagers"] = MaxVillagers, ["cursor_reminders"] = CursorReminders };
        File.WriteAllText(path, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
}
