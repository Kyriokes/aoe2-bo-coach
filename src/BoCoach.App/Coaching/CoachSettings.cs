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

    /// <summary>El nombre del jugador en el juego, para encontrarlo en la pantalla de carga; vacío = no se busca.</summary>
    public string PlayerName { get; set; } = "";

    /// <summary>Si ya se pasó (o se cerró) el tutorial de primer uso: la app no lo vuelve a abrir sola.</summary>
    public bool FirstRunDone { get; set; }

    /// <summary>D6: el coach arranca solo cuando se abre el juego y se detiene cuando se cierra.</summary>
    public bool AutoStart { get; set; } = true;

    /// <summary>D6: id de la última BO con que corrió el coach, para arrancar solo con ella.</summary>
    public string LastBuildOrder { get; set; } = "";

    /// <summary>D10: el Elo de 1v1 de mapa aleatorio del jugador (para las estadísticas de cruces), si lo puso.</summary>
    public int? Elo1v1 { get; set; }

    /// <summary>D10: el Elo de equipos de mapa aleatorio del jugador, si lo puso.</summary>
    public int? EloTeam { get; set; }

    /// <summary>D10: el perfil del juego que eligió con "Buscar" (para volver a buscarlo y comparar sus partidas).</summary>
    public long? ProfileId { get; set; }

    /// <summary>D10: el Elo más alto que se acepta (el mismo tope que la ventana).</summary>
    public const int MaxElo = 4000;

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
                    PlayerName = json["player_name"]?.GetValue<string>()?.Trim() ?? "",
                    FirstRunDone = json["first_run_done"]?.GetValue<bool>() ?? false,
                    AutoStart = json["auto_start"]?.GetValue<bool>() ?? true,
                    LastBuildOrder = json["last_build_order"]?.GetValue<string>()?.Trim() ?? "",
                    Elo1v1 = Number(json["elo_1v1"]) is { } elo1v1 and > 0 and <= MaxElo ? (int)elo1v1 : null,
                    EloTeam = Number(json["elo_team"]) is { } eloTeam and > 0 and <= MaxElo ? (int)eloTeam : null,
                    ProfileId = Number(json["profile_id"]) is { } profile and > 0 ? profile : null,
                };
            }
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidOperationException or FormatException)
        {
            // Un archivo roto no tiene que impedir abrir la app: quedan los valores por defecto.
        }
        return new CoachSettings();
    }

    /// <summary>Un número entero, o null si falta o es de otro tipo (un valor raro no tiene que tirar abajo los ajustes).</summary>
    private static long? Number(JsonNode? node) => node is JsonValue value && value.TryGetValue<long>(out var number) ? number : null;

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = new JsonObject { ["max_villagers"] = MaxVillagers, ["cursor_reminders"] = CursorReminders, ["civ"] = GameCiv, ["game_language"] = GameLanguage, ["player_name"] = PlayerName,
            ["first_run_done"] = FirstRunDone, ["auto_start"] = AutoStart, ["last_build_order"] = LastBuildOrder };
        if (Elo1v1 is { } elo1v1)
            json["elo_1v1"] = elo1v1;
        if (EloTeam is { } eloTeam)
            json["elo_team"] = eloTeam;
        if (ProfileId is { } profile)
            json["profile_id"] = profile;
        File.WriteAllText(path, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
}
