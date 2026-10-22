import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { beforeAll, describe, expect, it } from "vitest";
import { describeStep, type StepLike } from "../stepText";
import type { CoachRuntime } from "./runtime";
import { coachDir, repoDir as repo, startTestCoach } from "./testing";
import type { Analysis } from "./types";

/** El Core en WebAssembly corrido en Node (ver testing.ts). */
let coach: CoachRuntime;

beforeAll(async () => {
  coach = await startTestCoach();
}, 60_000);

describe("Core en WebAssembly", () => {
  it("carga los datos del juego", () => {
    expect(coach.datVersion).toMatch(/^VER \d+\.\d+/);
  });

  it("simula la BO de scouts igual que la app", () => {
    const result = coach.analyze(readFileSync(join(repo, "tests", "fixtures", "build_orders", "scouts_feudal.json"), "utf8"));

    expect(result.ok).toBe(true);
    const analysis = result as Analysis;
    expect(analysis.valid).toBe(true);
    expect(analysis.steps).toHaveLength(17);
    expect(analysis.steps[0].text).toBe("Construir 2 casas (3 constructores) → ovejas");
    expect(analysis.feudal_click).toBeGreaterThan(7 * 60);
    expect(analysis.feudal_click).toBeLessThan(10 * 60);
    expect(analysis.timeline?.length).toBeGreaterThan(10);
  });

  it("explica en español lo que está mal", () => {
    const broken = coach.analyze('{ "schema_version": 1, "name": "x", "steps": [ { "id": "a", "do": "train", "what": "dragon" } ] }');
    expect(broken).toMatchObject({ ok: true, valid: false, issues: [{ severity: "error", step_id: "a" }] });

    const notJson = coach.analyze("{ no es json");
    expect(notJson.ok).toBe(false);
  });
});

describe("los pasos escritos desde el servidor dicen lo mismo que el Core", () => {
  const texts = (json: string) => (coach.analyze(json) as Analysis).steps.map((step) => step.text);
  const described = (json: string) => (JSON.parse(json) as { steps: StepLike[] }).steps.map(describeStep);

  it("con cada entidad del juego, en singular y en plural, y cada forma de paso", () => {
    const gameData = JSON.parse(readFileSync(join(coachDir, "game_data.json"), "utf8")) as { entities: Record<string, unknown> };
    const steps: Record<string, unknown>[] = [];
    for (const what of [...Object.keys(gameData.entities), "dragon_volador"]) {
      steps.push({ id: `a${steps.length}`, do: "train", what, count: 1 });
      steps.push({ id: `a${steps.length}`, do: "build", what, count: 2, builders: 3, assign: "wood" });
      steps.push({ id: `a${steps.length}`, do: "research", what });
    }
    for (const assign of ["sheep", "hunt", "berries", "fish", "farm", "wood", "gold", "stone"]) {
      steps.push({ id: `a${steps.length}`, do: "train", what: "villager", count: 1, assign });
      steps.push({ id: `a${steps.length}`, do: "train", what: "villager", count: 6, assign });
      steps.push({ id: `a${steps.length}`, do: "move", count: 3, from: "sheep", assign });
      steps.push({ id: `a${steps.length}`, do: "move", count: 1, assign });
    }
    steps.push({ id: "p1", do: "research", what: "feudal_age", at_pop: 22 });
    steps.push({ id: "p2", do: "build", what: "house", builders: 1 });
    steps.push({ id: "p3", do: "train", what: "archer", count: 5, at_pop: 30 });
    const json = JSON.stringify({ schema_version: 2, name: "Todas", steps });

    expect(described(json)).toEqual(texts(json));
  });

  it("con las BO que trae la app (ejemplos y una por civ)", () => {
    const folders = [join(repo, "build_orders"), join(repo, "build_orders", "civs"), join(repo, "tests", "fixtures", "build_orders")];
    for (const folder of folders) {
      for (const file of readdirSync(folder).filter((f) => f.endsWith(".json"))) {
        const json = readFileSync(join(folder, file), "utf8");
        expect(described(json), file).toEqual(texts(json));
      }
    }
  });
});
