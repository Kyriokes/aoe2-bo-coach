import { beforeAll, describe, expect, it } from "vitest";
import { defaultPlan } from "../editor/assisted";
import { initialState, reduce, toJson } from "../editor/model";
import type { CoachRuntime } from "./runtime";
import { startTestCoach } from "./testing";
import type { Analysis } from "./types";

/** D12 (e): el modo asistido por el mismo Analyze que usa el editor, con el Core en WebAssembly en Node. */
let coach: CoachRuntime;

beforeAll(async () => {
  coach = await startTestCoach();
}, 60_000);

const bo = (civ: string, until: number, goals: Record<string, unknown>[]) =>
  JSON.stringify({ schema_version: 1, id: "asis", name: "Asistida", civ, tags: [], steps: [], assisted: { villagers: { until }, goals } });

describe("el modo asistido en el Core", () => {
  it("Analyze genera los pasos y dice cuándo se cumple cada objetivo", () => {
    const result = coach.analyze(bo("franks", 60, [{ age: "feudal_age" }, { unit: "scout_cavalry", count: 20, buildings: 2 }])) as Analysis;

    expect(result.valid).toBe(true);
    expect(result.steps.length).toBeGreaterThan(5);
    expect(result.assisted?.goals.every((g) => g.end !== null)).toBe(true);
    expect(result.assisted?.reasons[result.steps[0].id]).toBeTruthy();
    expect(result.build_order.assisted).toBeTruthy();
  });

  it("una civ que no tiene el objetivo lo deja en rojo con el motivo", () => {
    const result = coach.analyze(bo("incas", 60, [{ age: "feudal_age" }, { unit: "scout_cavalry", count: 20, buildings: 2 }])) as Analysis;

    expect(result.assisted?.goals[1].problem).toContain("no tienen");
    expect(result.assisted?.goals[0].end).not.toBeNull();
  });

  it("una BO guardada por la web no avisa que se regeneró al volver a abrirla", () => {
    // Revisión del (e): la web guardaba los pasos generados sin la postura que escribe el generador.
    let state = reduce(reduce(initialState(), { type: "new", id: "nueva" }), { type: "setAssisted", assisted: defaultPlan() });
    state = reduce(state, { type: "setField", field: "civ", value: "franks" });
    const generated = coach.analyze(JSON.stringify(toJson({ bo: state.bo, steps: [] }))) as Analysis;
    state = reduce(state, { type: "generated", analysis: generated, revision: state.revision });

    const reopened = coach.analyze(JSON.stringify(toJson(state))) as Analysis;

    expect(state.bo.posture).toEqual(generated.build_order.posture);
    expect(reopened.assisted?.regenerated).toBe(false);
  });

  it("120 aldeanos hasta Imperial con 5 objetivos: cuánto tarda", () => {
    const json = bo("franks", 120, [
      { age: "feudal_age" },
      { unit: "scout_cavalry", count: 20, buildings: 2 },
      { age: "castle_age" },
      { town_centers: 3 },
      { age: "imperial_age" },
    ]);
    coach.analyze(json);
    const start = performance.now();
    const result = coach.analyze(json) as Analysis;
    const ms = performance.now() - start;
    console.log(`Analyze asistido: ${Math.round(ms)} ms`);

    expect(result.assisted?.problems).toEqual([]);
    expect(ms).toBeLessThan(3000); // la meta de la web (300 ms) se mide en el navegador; en Node, con el intérprete, sirve de techo
  });
});
