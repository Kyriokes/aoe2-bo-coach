import { describe, expect, it } from "vitest";
import { stallsByStep } from "./stalls";
import type { PlanBlock } from "./types";

const block = (step_id: string, from: number, to: number, extra: Partial<PlanBlock> = {}): PlanBlock => ({
  step_id,
  reason: "resources",
  from,
  to,
  missing: "wood",
  expected: false,
  ...extra,
});

describe("lo que se muestra como \"Se traba\"", () => {
  it("junta los trabones de 5 s o más de cada paso", () => {
    const stalls = stallsByStep([block("s1", 10, 40), block("s1", 60, 64), block("s2", 0, 5)]);

    expect(stalls.get("s1")).toEqual([block("s1", 10, 40)]);
    expect(stalls.get("s2")).toEqual([block("s2", 0, 5)]);
  });

  it("deja afuera las esperas de algo que ya viene", () => {
    const stalls = stallsByStep([block("s1", 0, 100, { reason: "villagers", missing: null, expected: true })]);

    expect(stalls.size).toBe(0);
  });
});
