import { describe, expect, it, vi } from "vitest";
import { getDictionary } from "@/i18n";
import type { Analysis } from "../coach/types";
import { addGoal, debouncer, defaultPlan, diffGoals, fieldChangeLabel, goalLabel, moveGoal, newAssisted, planFromJson, removeGoal, setUntil, toManual, updateGoal } from "./assisted";
import { initialState, isDirty } from "./model";

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

  it("al mover objetivos, cada uno se compara consigo mismo y dice su lugar nuevo", () => {
    const before = defaultPlan();
    const after = moveGoal(before, 1, 0);

    const strip = diffGoals(analysis([589, 1100]), analysis([1150, 620]), "Castillos más arriba", after, before);

    expect(strip?.shifts.map((s) => [s.index, s.delta])).toEqual([
      [0, 50],
      [1, 31],
    ]);
  });

  it("sin análisis anterior no hay franja", () => {
    expect(diffGoals(null, analysis([589]), "x", defaultPlan(), null)).toBeNull();
  });

  // Antes escribía la clave cruda: "civ britons", y el nombre del campo en inglés también en castellano ("name").
  it("nombra lo que se tocó en el idioma de la página, no con su clave", () => {
    const maps = [{ key: "arabia", name: "Arabia" }, { key: "black_forest", name: "Bosque Negro" }];
    expect(fieldChangeLabel("civ", "britons", maps)).toBe("civ Britanos");
    expect(fieldChangeLabel("civ", "britons", maps, "en")).toBe("civ Britons");
    expect(fieldChangeLabel("civ", "any", maps)).toBe("civ Cualquiera");
    expect(fieldChangeLabel("civ", "any", maps, "en")).toBe("civ Any");
    expect(fieldChangeLabel("map", "black_forest", maps)).toBe("mapa Bosque Negro");
    expect(fieldChangeLabel("map", "otro", maps, "en")).toBe("map otro");
    expect(fieldChangeLabel("name", "x", maps)).toBe("Nombre");
    expect(fieldChangeLabel("description", "x", maps, "en")).toBe("Description");
  });
});

describe("una BO nueva", () => {
  it("se llama como en el idioma de la página", () => {
    expect(newAssisted(initialState(), "n1", getDictionary("en").editor.newName).bo.name).toBe("New build order");
    expect(newAssisted(initialState(), "n1", getDictionary("es").editor.newName).bo.name).toBe("Nueva build order");
  });

  it("arranca sin cambios para guardar", () => {
    expect(isDirty(newAssisted(initialState(), "n1", "x"))).toBe(false);
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
