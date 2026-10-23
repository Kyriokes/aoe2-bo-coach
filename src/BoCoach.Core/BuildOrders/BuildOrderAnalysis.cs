using System.Text;
using System.Text.Json;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.BuildOrders;

/// <summary>
/// Todo lo que la web muestra de una BO, en una sola pasada y como JSON (para pasarlo a JavaScript sin depender de
/// tipos): la BO leída, los problemas de validación, cada paso descrito en español, la simulación del Planner
/// (inicio y fin de cada paso, bloqueos y muestras del stock) y los clics de edad estimados. Es lo mismo que ve el
/// editor de la app.
/// </summary>
public static class BuildOrderAnalysis
{
    /// <param name="maps">Los mapas (data/maps.json), para los avisos del mapa de la BO (D8).</param>
    public static string Analyze(string json, GameData data, EventCatalog? events = null, Maps? maps = null)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            BuildOrder bo;
            try
            {
                bo = BuildOrderSerializer.Parse(json);
            }
            catch (BuildOrderFormatException e)
            {
                w.WriteBoolean("ok", false);
                w.WriteString("error", e.Message);
                w.WriteEndObject();
                w.Flush();
                return Encoding.UTF8.GetString(stream.ToArray());
            }

            var validation = new BuildOrderValidator(data, events, maps).Validate(bo);
            w.WriteBoolean("ok", true);
            w.WriteBoolean("valid", validation.IsValid);
            WriteBuildOrder(w, bo);
            WriteStart(w, bo, data);
            WriteIssues(w, validation);

            // Una BO con errores puede no simularse (por ejemplo, una entidad que no existe).
            var plan = validation.IsValid ? new Planner(data).Simulate(bo) : null;
            WriteSteps(w, bo, plan);
            if (plan is not null)
                WritePlan(w, plan);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteBuildOrder(Utf8JsonWriter w, BuildOrder bo)
    {
        w.WriteString("id", bo.Id);
        w.WriteString("name", bo.Name);
        w.WriteString("civ", bo.Civ);
        w.WriteString("map", bo.Map);
        w.WriteString("author", bo.Author);
        w.WriteString("description", bo.Description);
        w.WriteNumber("schema_version", bo.SchemaVersion);
        w.WriteStartArray("tags");
        foreach (var tag in bo.Tags)
            w.WriteStringValue(tag);
        w.WriteEndArray();
        w.WriteStartObject("targets");
        if (bo.Targets?.FeudalClickTime is { } feudal)
            w.WriteNumber("feudal_click_time", feudal);
        if (bo.Targets?.CastleClickTime is { } castle)
            w.WriteNumber("castle_click_time", castle);
        w.WriteEndObject();
        // La postura de cierre (D2), escrita o deducida de los pasos.
        var (posture, derived) = Posture.Effective(bo);
        w.WriteStartObject("posture");
        w.WriteString("text", posture.Queue.Count == 0 ? null
            : $"{string.Join(" y ", posture.Queue.Select(u => StepText.Name(u, 2)))} siempre en cola");
        w.WriteString("next_age", posture.NextAge is { } next ? StepText.Name(next) : null);
        w.WriteBoolean("derived", derived);
        w.WriteEndObject();
        // Las ramas (D3): la señal, el límite, la nota y sus pasos descritos.
        w.WriteStartArray("branches");
        foreach (var branch in bo.Branches ?? [])
        {
            w.WriteStartObject();
            w.WriteString("on", Branch.Signals.GetValueOrDefault(branch.On, branch.On));
            if (branch.Before is { } before)
                w.WriteString("before", GameClock.Format(before));
            w.WriteString("note", branch.Note);
            w.WriteStartArray("steps");
            foreach (var step in branch.Steps)
                w.WriteStringValue(StepText.Describe(step));
            w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndArray();
        // La BO normalizada (migrada a la versión actual), para guardarla tal cual.
        w.WritePropertyName("build_order");
        using var doc = JsonDocument.Parse(BuildOrderSerializer.Serialize(bo));
        doc.RootElement.WriteTo(w);
    }

    /// <summary>
    /// Con qué arranca la partida para la civ de la BO (lo que usa el Planner): recursos, población, los aldeanos sin
    /// tarea (los 3 iniciales) y los que ya trabajan (los de más de algunas civs, en las ovejas), y si hace casas. El
    /// editor de la web lo muestra en la caja "Inicio" (D8).
    /// </summary>
    private static void WriteStart(Utf8JsonWriter w, BuildOrder bo, GameData data)
    {
        var start = PlanStart.For(data.Civ(bo.Civ), data);
        w.WriteStartObject("start");
        w.WriteNumber("food", Math.Round(start.Stock.Food));
        w.WriteNumber("wood", Math.Round(start.Stock.Wood));
        w.WriteNumber("gold", Math.Round(start.Stock.Gold));
        w.WriteNumber("stone", Math.Round(start.Stock.Stone));
        w.WriteNumber("population", start.Population);
        w.WriteNumber("population_cap", start.PopulationCap);
        w.WriteNumber("idle_villagers", start.IdleVillagers);
        w.WriteStartObject("villagers");
        foreach (var (source, count) in start.Villagers.Where(v => v.Value > 0))
            w.WriteNumber(source.ToString().ToLowerInvariant(), count);
        w.WriteEndObject();
        w.WriteBoolean("houses", data.CanMake(bo.Civ, "house"));
        w.WriteEndObject();
    }

    private static void WriteIssues(Utf8JsonWriter w, ValidationResult validation)
    {
        w.WriteStartArray("issues");
        foreach (var issue in validation.Issues)
        {
            w.WriteStartObject();
            w.WriteString("severity", issue.Severity == Severity.Error ? "error" : "warning");
            w.WriteString("step_id", issue.StepId);
            w.WriteString("message", issue.Message);
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    private static void WriteSteps(Utf8JsonWriter w, BuildOrder bo, PlanResult? plan)
    {
        // D8: las unidades de cada paso (aldeanos nuevos en los de aldeanos, donde count es el total en la fuente), para
        // que el editor de la web cargue cualquier BO a su modelo en unidades.
        var units = Engine.LivePlan.Units(bo);
        w.WriteStartArray("steps");
        for (var i = 0; i < bo.Steps.Count; i++)
        {
            var step = bo.Steps[i];
            var estimate = plan?.For(step.Id);
            w.WriteStartObject();
            w.WriteString("id", step.Id);
            w.WriteString("text", StepText.Describe(step));
            w.WriteString("do", step.Do.ToString().ToLowerInvariant());
            w.WriteString("what", step.What);
            w.WriteNumber("count", step.Count);
            w.WriteNumber("units", units[i]);
            w.WriteString("note", step.Note);
            Number(w, "start", estimate?.Start);
            Number(w, "end", estimate?.End);
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    private static void WritePlan(Utf8JsonWriter w, PlanResult plan)
    {
        w.WriteBoolean("completed", plan.Completed);
        Number(w, "feudal_click", plan.ClickTime("feudal_age"));
        Number(w, "castle_click", plan.ClickTime("castle_age"));
        Number(w, "imperial_click", plan.ClickTime("imperial_age"));

        w.WriteStartArray("blocks");
        foreach (var block in plan.Blocks)
        {
            w.WriteStartObject();
            w.WriteString("step_id", block.StepId);
            w.WriteString("reason", block.Reason.ToString().ToLowerInvariant());
            w.WriteNumber("from", block.From);
            w.WriteNumber("to", block.To);
            w.WriteString("missing", block.Missing?.ToString().ToLowerInvariant());
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteStartArray("timeline");
        foreach (var sample in plan.Timeline)
        {
            w.WriteStartObject();
            w.WriteNumber("time", sample.Time);
            w.WriteNumber("food", Math.Round(sample.Stock.Food));
            w.WriteNumber("wood", Math.Round(sample.Stock.Wood));
            w.WriteNumber("gold", Math.Round(sample.Stock.Gold));
            w.WriteNumber("stone", Math.Round(sample.Stock.Stone));
            w.WriteNumber("population", sample.Population);
            w.WriteNumber("population_cap", sample.PopulationCap);
            w.WriteNumber("villagers", sample.Villagers);
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    private static void Number(Utf8JsonWriter w, string name, double? value)
    {
        if (value is { } v)
            w.WriteNumber(name, v);
        else
            w.WriteNull(name);
    }
}
