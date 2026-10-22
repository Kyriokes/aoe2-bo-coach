import { describe, expect, it } from "vitest";
import type { Analysis } from "../coach/types";
import type { GameDataLite } from "./constraints";
import { lastWait, waitReason } from "./explain";
import type { EditorStep } from "./model";

const data: GameDataLite = {
  entities: { feudal_age: { kind: "tech", age: "dark", made_in: "town_center", cost: { food: 500 } }, archer: { kind: "unit", age: "feudal", made_in: "archery_range", cost: { wood: 25, gold: 45 } } },
};

function analysis(extra: Partial<Analysis>): Analysis {
  return {
    ok: true,
    valid: true,
    id: "x",
    name: "x",
    civ: "any",
    map: null,
    author: null,
    description: null,
    schema_version: 2,
    tags: [],
    targets: {},
    build_order: {},
    issues: [],
    steps: [],
    ...extra,
  };
}

const feudal: EditorStep = { id: "f", do: "research", what: "feudal_age", count: 1, after: [], withPrevious: false };
const archers: EditorStep = { id: "a", do: "train", what: "archer", count: 4, after: [], withPrevious: false };

describe("por qué espera una caja", () => {
  it("con un bloqueo por recursos estima cuánto falta con el stock de la muestra más cercana", () => {
    const a = analysis({
      blocks: [{ step_id: "f", reason: "resources", from: 400, to: 475, missing: "food" }],
      timeline: [
        { time: 390, food: 120, wood: 0, gold: 0, stone: 0, population: 20, population_cap: 25, villagers: 19 },
        { time: 410, food: 160, wood: 0, gold: 0, stone: 0, population: 20, population_cap: 25, villagers: 19 },
      ],
    });
    expect(waitReason(a, [feudal], data)).toBe("faltan 380 de comida");
    expect(waitReason(a, [feudal], null)).toBe("falta comida");
    expect(lastWait(a, [feudal])?.reason).toBe("resources");
  });

  it("la cantidad cuenta las unidades del paso", () => {
    const a = analysis({
      blocks: [{ step_id: "a", reason: "resources", from: 600, to: 650, missing: "gold" }],
      timeline: [{ time: 600, food: 0, wood: 0, gold: 100, stone: 0, population: 20, population_cap: 25, villagers: 19 }],
    });
    expect(waitReason(a, [archers], data)).toBe("faltan 80 de oro"); // 4 arqueros × 45 − 100
  });

  it("sin bloqueo, usa el aviso del validador sobre la población (el Planner no deja rastro de esa espera)", () => {
    const a = analysis({ issues: [{ severity: "warning", step_id: "f", message: "La población nunca llega a 22: con los pasos anteriores hay 19." }] });
    expect(waitReason(a, [feudal], data)).toBe("La población nunca llega a 22: con los pasos anteriores hay 19.");
    expect(lastWait(a, [feudal])).toBeNull();
    expect(waitReason(analysis({}), [feudal], data)).toBeNull();
  });

  it("el último bloqueo manda, y los de otros pasos no cuentan", () => {
    const a = analysis({
      blocks: [
        { step_id: "a", reason: "population", from: 100, to: 130, missing: null },
        { step_id: "a", reason: "producer", from: 130, to: 3600, missing: null },
        { step_id: "f", reason: "resources", from: 200, to: 5000, missing: "food" },
      ],
    });
    expect(lastWait(a, [archers])?.reason).toBe("producer");
    expect(waitReason(a, [archers], data)).toBe("falta el edificio que lo produce");
  });
});
