import { readFileSync } from "node:fs";
import { join } from "node:path";
import { pathToFileURL } from "node:url";
import { beforeAll, describe, expect, it } from "vitest";
import { startCoach, type CoachRuntime } from "./runtime";
import type { Analysis } from "./types";

/**
 * El Core en WebAssembly corrido en Node: el mismo runtime que carga el navegador desde public/coach
 * (hay que correr antes `npm run coach`).
 */
const coachDir = join(__dirname, "..", "..", "..", "public", "coach");
const repo = join(__dirname, "..", "..", "..", "..");

let coach: CoachRuntime;

beforeAll(async () => {
  const dotnetModule = await import(pathToFileURL(join(coachDir, "_framework", "dotnet.js")).href);
  coach = await startCoach(
    dotnetModule,
    readFileSync(join(coachDir, "game_data.json"), "utf8"),
    readFileSync(join(coachDir, "events.json"), "utf8"),
  );
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
