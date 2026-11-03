import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { parseBuildOrder } from "@/lib/buildOrder";
import { dbErrorMessage } from "@/lib/dbErrors";
import { paletteLabel, blockReason, ageLabel, neverStartsText, notBeforeText } from "@/lib/editor/text";
import { withArticle } from "@/lib/editor/guard";
import { goalLabel } from "@/lib/editor/assisted";
import { parseMaps, paletteItems } from "@/lib/editor/gameData";
import { allowedRange } from "@/lib/editor/constraints";
import { guideLabels } from "@/lib/editor/villagers";
import { STANDARD_START } from "@/lib/editor/opening";
import type { EditorStep } from "@/lib/editor/model";
import { chanceText, durationLabel, eloLabel, mapName, type MatchupData } from "@/lib/matchups";
import { matchupPageMeta, mapLine, selectionPlace } from "@/lib/matchupView";
import { byline, civLabel, shareFacts } from "@/lib/share";
import { parseSupport, formatAmount } from "@/lib/support";

/** Las funciones puras que arman texto, en inglés. En castellano las controlan los tests de cada una. */
describe("los textos armados en inglés", () => {
  const bo = { name: "x", civ: "franks", author: "Ana", uploader: "Luz", description: null, feudal_click: 490, castle_click: null };

  it("share", () => {
    expect(civLabel("any", "en")).toBe("Any civ");
    expect(civLabel("hindustanis", "en")).toBe("Hindustanis");
    expect(byline(bo, "en")).toBe("Ana, uploaded by Luz");
    expect(shareFacts(bo, "en")).toBe("Franks · Feudal 8:10 · by Ana, uploaded by Luz");
    expect(byline({ author: "", uploader: "" }, "en")).toBe("Anonymous");
    expect(byline({ author: "", uploader: "" }, "es")).toBe("Anónimo");
  });

  it("errores", () => {
    expect(parseBuildOrder("{ no", "en")).toEqual({ ok: false, error: "That isn't valid JSON." });
    expect(parseBuildOrder({ schema_version: 2, name: "x", steps: [] }, "en")).toMatchObject({ error: "The BO has no steps. (steps)" });
    expect(dbErrorMessage({ code: "P0002", message: "No hay ninguna" }, "en")).toBe("That build order doesn't exist or isn't public.");
    expect(dbErrorMessage({ code: "P0002", message: "No hay ninguna" }, "es")).toBe("No hay ninguna");
    expect(dbErrorMessage({ message: "boom" }, "en")).toBe("Database error. Try again in a while.");
  });

  it("editor", () => {
    expect(ageLabel("castle", "en")).toBe("Castle");
    expect(blockReason({ reason: "resources", missing: "food" }, 500, "en")).toBe("needs 500 more food");
    expect(blockReason({ reason: "population", missing: null }, null, "en")).toBe("needs population (houses)");
    expect(notBeforeText(490, "x", "en")).toBe("Not before 8:10: x");
    expect(neverStartsText(null, "en")).toBe("It doesn't start here: the previous step doesn't finish either");
    expect(paletteLabel("age", "castle", "en")).toBe("Click up to Castle");
    expect(paletteLabel("villagers", "wood", "en")).toBe("Villagers to wood");
    expect(goalLabel({ town_centers: 3 }, undefined, "en")).toBe("3 town centers");
    expect(goalLabel({ unit: "archer", count: 10, buildings: 2 }, () => "archery_range", "en")).toBe("10 archers in 2 archery ranges");
  });

  it("el artículo en inglés depende de la primera letra", () => {
    expect(withArticle("stable", "en")).toBe("a stable");
    expect(withArticle("archery_range", "en")).toBe("an archery range");
    expect(withArticle("outpost", "en")).toBe("an outpost");
    expect(withArticle("university", "en")).toBe("a university");
    expect(withArticle("archery_range", "es")).toBe("una galería de tiro");
    expect(withArticle("stable", "es")).toBe("un establo");
  });

  it("los pasos como en las guías", () => {
    const steps: EditorStep[] = [
      { id: "a", do: "train", what: "villager", count: 3, assign: "sheep", after: [], withPrevious: false },
      { id: "b", do: "train", what: "villager", count: 1, assign: "wood", after: [], withPrevious: false },
    ];
    const labels = guideLabels(steps, STANDARD_START, "en");
    expect(labels.get("a")).toBe("Villagers 4 to 6 → sheep");
    expect(labels.get("b")).toBe("Villager 7 → wood");
  });

  it("los motivos del editor no se quedan en castellano", () => {
    const data = { entities: { archer: { kind: "unit", age: "feudal", made_in: "archery_range", cost: {} } }, civs: {} } as never;
    const step: EditorStep = { id: "x", do: "train", what: "archer", count: 1, after: [], withPrevious: false };
    expect(allowedRange([], [step], data, undefined, "en").loReason).toBe("Only after clicking up to Feudal");
  });

  it("cruces", () => {
    const data = { eloLimits: [850, 1050, 1300, 1600], minuteLimits: [20, 30, 40, 55] } as MatchupData;
    expect(chanceText({ p: 0.49, from: 0.46, to: 0.53, n: 1 }, "en")).toBe("49% (46 to 53)");
    expect(eloLabel(data, 1, "en")).toBe("under 850");
    expect(eloLabel(data, 5, "en")).toBe("1600 or more");
    expect(durationLabel(data, 3, "en")).toBe("30 to 40 min");
    expect(mapName("black_forest", "en")).toBe("Black Forest");
    expect(selectionPlace("Nomad", 2, "en")).toBe("Nomad in that Elo band");
    expect(mapLine({ p: 0, from: 0, to: 0, n: 0 }, "Nomad", "en")).toBe("Estimated: there are no games of this matchup on Nomad.");
    expect(matchupPageMeta("britons-vs-franks", "en")).toMatchObject({ title: "Britons vs Franks: matchup stats", path: "/cruces/britons-vs-franks" });
    expect(matchupPageMeta("britons-vs-franks", "es")?.title).toBe("Britanos contra francos: estadísticas del cruce");
  });

  it("todos los mapas tienen nombre en inglés", () => {
    const source = readFileSync(join(__dirname, "..", "..", "..", "data", "maps.json"), "utf8");
    const es = parseMaps(source, "es");
    const en = parseMaps(source, "en");
    expect(en.map((m) => m.key)).toEqual(es.map((m) => m.key));
    expect(en.find((m) => m.key === "black_forest")?.name).toBe("Black Forest");
    expect(es.find((m) => m.key === "black_forest")?.name).toBe("Bosque Negro");
    expect(en.find((m) => m.key === "arena")?.note).toMatch(/^Walled/);
    const names = (JSON.parse(source.replace(/^\s*\/\/.*$/gm, "")) as { maps: Record<string, { name_en?: string; note?: string; note_en?: string }> }).maps;
    for (const [key, map] of Object.entries(names)) {
      expect(map.name_en, key).toBeTruthy();
      if (map.note) expect(map.note_en, key).toBeTruthy();
    }
  });

  it("la paleta sale en el idioma", () => {
    const data = { entities: { house: { kind: "building", age: "dark", cost: {} } }, civs: {} } as never;
    const groups = paletteItems(data, "any", null, "en");
    expect(groups.map((g) => g.title)).toEqual(["Villagers", "Move", "Buildings", "Research", "Units"]);
    expect(groups[2].items[0].label).toBe("House");
  });

  it("las metas de las donaciones traen su inglés, y el castellano si falta", () => {
    const source = JSON.stringify({ goals: [{ id: "a", name: "Dominio", name_en: "Domain", description: "Uno", target: 10 }] });
    expect(parseSupport(source, "en").goals[0]).toMatchObject({ name: "Domain", description: "Uno" });
    expect(parseSupport(source, "es").goals[0].name).toBe("Dominio");
    expect(formatAmount(1000, "USD", "en")).toBe("$1,000");
  });
});
