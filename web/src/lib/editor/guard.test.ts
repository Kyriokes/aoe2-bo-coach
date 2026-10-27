import { describe, expect, it } from "vitest";
import type { Analysis } from "../coach/types";
import { brokenBy } from "./guard";
import type { EditorStep } from "./model";

const step = (id: string, fields: Partial<EditorStep> & Pick<EditorStep, "do">): EditorStep => ({ id, count: 1, after: [], withPrevious: false, ...fields });

/** Un análisis chico: los pasos con su hora de arranque (null = no arranca) y los bloqueos. */
const analysis = (starts: Record<string, number | null>, blocks: Analysis["blocks"] = []): Analysis =>
  ({
    ok: true,
    valid: true,
    completed: Object.values(starts).every((s) => s !== null),
    issues: [],
    steps: Object.entries(starts).map(([id, start]) => ({ id, start, end: start, text: id })),
    blocks,
  }) as unknown as Analysis;

describe("no lo deja y explica", () => {
  it("un paso nuevo que no arranca se rechaza aunque la BO ya tuviera otro trabado", () => {
    const before = analysis({ a: null, b: 10 });
    const after = analysis({ a: null, b: 10, c: null }, [{ step_id: "c", reason: "producer", from: 600, to: 3600, missing: null, expected: false }]);
    const steps = [step("a", { do: "build", what: "stable" }), step("b", { do: "research", what: "feudal_age" }), step("c", { do: "research", what: "castle_age" })];

    expect(brokenBy(before, after, steps, null)).toBe('Así "c" no arranca nunca: faltan 2 edificios de Feudal terminados antes (galería de tiro, establo, herrería, mercado)');
  });

  it("lo que ya estaba trabado no frena otras ediciones", () => {
    expect(brokenBy(analysis({ a: null, b: 10 }), analysis({ a: null, b: 12 }), [step("a", { do: "build", what: "stable" }), step("b", { do: "build", what: "house" })], null)).toBeNull();
  });
});
