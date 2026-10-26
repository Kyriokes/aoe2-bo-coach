import { describe, expect, it } from "vitest";
import { blockLines } from "./stalls";
import type { PlanBlock } from "./types";

const block = (step_id: string, from: number, to: number, extra: Partial<PlanBlock> = {}): PlanBlock => ({
  step_id,
  reason: "population",
  from,
  to,
  missing: null,
  expected: false,
  ...extra,
});

describe("los renglones de bloqueo de cada paso", () => {
  it("un trabón de 5 s o más es \"Se traba\"", () => {
    const lines = blockLines([block("s1", 10, 40), block("s1", 60, 64), block("s2", 0, 5, { reason: "producer" })]);

    expect(lines.get("s1")).toEqual([{ kind: "stall", block: block("s1", 10, 40) }]);
    expect(lines.get("s2")).toEqual([{ kind: "stall", block: block("s2", 0, 5, { reason: "producer" }) }]);
  });

  it("juntar recursos es una espera, y solo si dura 30 s o más", () => {
    const long = block("s1", 100, 130, { reason: "resources", missing: "wood" });
    const lines = blockLines([long, block("s2", 0, 29, { reason: "resources", missing: "food" })]);

    expect(lines.get("s1")).toEqual([{ kind: "wait", block: long }]);
    expect(lines.has("s2")).toBe(false);
  });

  it("deja afuera las esperas de algo que ya viene", () => {
    const lines = blockLines([block("s1", 0, 100, { reason: "villagers", expected: true })]);

    expect(lines.size).toBe(0);
  });
});
