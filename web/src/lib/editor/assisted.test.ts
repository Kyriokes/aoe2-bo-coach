import { describe, expect, it, vi } from "vitest";
import type { Analysis } from "../coach/types";
import { addGoal, debouncer, defaultPlan, diffGoals, goalLabel, moveGoal, planFromJson, removeGoal, setUntil, toManual, updateGoal } from "./assisted";

const scouts = { unit: "scout_cavalry", count: 20, buildings: 2 };
const stableOf = (unit: string) => (unit === "scout_cavalry" ? "stable" : undefined);

describe("los objetivos del modo asistido", () => {
  it("una BO nueva arranca con aldeanos hasta 120, Feudal y Castillos", () => {
    const plan = defaultPlan();
    expect(plan.villagers.until).toBe(120);
    expect(plan.goals.map((g) => g.age)).toEqual(["feudal_age", "castle_age"]);
  });

  it("agrega, mueve, cambia y quita objetivos", () => {
    let plan = addGoal(defaultPlan(), scouts);
    expect(plan.goals).toHaveLength(3);

    plan = moveGoal(plan, 2, 1);
    expect(plan.goals.map((g) => g.age ?? g.unit)).toEqual(["feudal_age", "scout_cavalry", "castle_age"]);

    plan = updateGoal(plan, 1, { buildings: 30, count: 0 });
    expect(plan.goals[1]).toMatchObject({ buildings: 10, count: 1 });

    plan = updateGoal(plan, 2, { at_pop: 32 });
    expect(plan.goals[2].at_pop).toBe(32);
    plan = updateGoal(plan, 2, { at_pop: undefined });
    expect(plan.goals[2]).not.toHaveProperty("at_pop");

    plan = removeGoal(plan, 0);
    expect(plan.goals.map((g) => g.age ?? g.unit)).toEqual(["scout_cavalry", "castle_age"]);
    expect(setUntil(plan, 500).villagers.until).toBe(200);
  });

  it("nombra cada objetivo como en la lista", () => {
    expect(goalLabel({ age: "castle_age" })).toBe("Edad de los Castillos");
    expect(goalLabel(scouts, stableOf)).toBe("20 exploradores en 2 establos");
    expect(goalLabel({ town_centers: 3 })).toBe("3 centros urbanos");
  });

  it("lee el JSON del Core con lo que falte por defecto", () => {
    const plan = planFromJson({ villagers: { until: 60 }, goals: [{ age: "feudal_age" }, 7] });
    expect(plan?.villagers.until).toBe(60);
    expect(plan?.opening.sheep).toBe(6);
    expect(plan?.goals).toEqual([{ age: "feudal_age" }]);
    expect(planFromJson(null)).toBeNull();
  });
});

const analysis = (ends: (number | null)[]): Analysis =>
  ({
    ok: true,
    valid: true,
    assisted: { version: 1, regenerated: false, reasons: {}, problems: [], goals: ends.map((end, index) => ({ index, start: null, end, problem: null })) },
  }) as unknown as Analysis;

describe("la franja Último cambio", () => {
  it("dice qué se tocó y cómo se corrió cada objetivo", () => {
    const before = addGoal(defaultPlan(), scouts);
    const after = updateGoal(before, 2, { buildings: 3 });

    const strip = diffGoals(analysis([589, 1100, 1100]), analysis([589, 1140, 1060]), "establos 2 a 3", after, before, stableOf);

    expect(strip?.change).toBe("establos 2 a 3");
    expect(strip?.lines).toEqual(["Edad de los Castillos 18:20 a 19:00 (+0:40)", "20 exploradores en 3 establos 18:20 a 17:40 (−0:40)"]);
  });

  it("sin análisis anterior no hay franja", () => {
    expect(diffGoals(null, analysis([589]), "x", defaultPlan(), null)).toBeNull();
  });
});

describe("pasar a manual", () => {
  it("deja la BO sin assisted y con los pasos generados", () => {
    const generated = {
      ok: true,
      valid: true,
      id: "a",
      name: "Asistida",
      civ: "franks",
      map: null,
      author: null,
      description: null,
      tags: [],
      build_order: { assisted: { villagers: { until: 60 } }, steps: [{ id: "s1", do: "train", what: "villager", count: 3, assign: "sheep" }] },
      steps: [{ id: "s1", units: 3 }],
    } as unknown as Analysis;

    const { bo, steps } = toManual(generated);

    expect(bo).not.toHaveProperty("assisted");
    expect(steps.map((s) => s.id)).toEqual(["s1"]);
  });
});

describe("los toques seguidos", () => {
  it("se juntan en una sola simulación", () => {
    vi.useFakeTimers();
    const run = vi.fn();
    const d = debouncer(150);
    for (let i = 0; i < 5; i++) {
      d.schedule(run);
      vi.advanceTimersByTime(100);
    }
    expect(run).not.toHaveBeenCalled();
    vi.advanceTimersByTime(150);
    expect(run).toHaveBeenCalledTimes(1);
    vi.useRealTimers();
  });
});
