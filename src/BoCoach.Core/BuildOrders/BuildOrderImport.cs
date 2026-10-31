using System.Text;
using System.Text.Json;
using BoCoach.Core.Data;

namespace BoCoach.Core.BuildOrders;

/// <summary>Una BO leída de un JSON cualquiera, con los avisos del importador y de qué formato venía.</summary>
/// <param name="Source">"bocoach" (el formato de la app), "rts_overlay" o "buildorderguide".</param>
public sealed record ImportedBuildOrder(BuildOrder BuildOrder, IReadOnlyList<string> Warnings, string Source);

/// <summary>
/// Reconoce el formato de un JSON de BO (el de la app, una guía de RTS Overlay o una de buildorderguide.com) y lo
/// convierte. Lo usan "Importar…" de la app y "Abrir" o "Pegar" del editor de la web.
/// </summary>
public static class BuildOrderImport
{
    public static ImportedBuildOrder Read(string json, GameData data)
    {
        if (RtsOverlayImporter.IsRtsOverlay(json))
        {
            var (bo, warnings) = RtsOverlayImporter.Convert(json, data);
            return new ImportedBuildOrder(bo, warnings, "rts_overlay");
        }
        if (BuildOrderGuideImporter.IsBuildOrderGuide(json))
        {
            var (bo, warnings) = BuildOrderGuideImporter.Convert(json, data);
            return new ImportedBuildOrder(bo, warnings, "buildorderguide");
        }
        return new ImportedBuildOrder(BuildOrderSerializer.Parse(json), [], "bocoach");
    }

    /// <summary>
    /// <see cref="Read"/> como JSON para la web: <c>{"ok":true,"source":...,"warnings":[...],"build_order":{...}}</c>
    /// o <c>{"ok":false,"error":...}</c>. No tira.
    /// </summary>
    public static string ReadJson(string json, GameData data)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            try
            {
                var imported = Read(json, data);
                var serialized = BuildOrderSerializer.Serialize(imported.BuildOrder);
                w.WriteBoolean("ok", true);
                w.WriteString("source", imported.Source);
                w.WriteStartArray("warnings");
                foreach (var warning in imported.Warnings)
                    w.WriteStringValue(warning);
                w.WriteEndArray();
                w.WritePropertyName("build_order");
                w.WriteRawValue(serialized);
            }
            catch (Exception e) when (e is BuildOrderFormatException or JsonException or FormatException or InvalidOperationException)
            {
                w.WriteBoolean("ok", false);
                w.WriteString("error", e.Message);
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
