import { formatClock } from "../buildOrder";
import type { Analysis } from "../coach/types";
import { entityName } from "../stepText";
import { fromAnalysis, type EditorBo, type EditorStep } from "./model";

/**
 * El modo asistido del editor (D12, pantalla A de la spec): el campo `assisted` de la BO con sus objetivos, las
 * acciones sobre la lista, la franja "Último cambio" y el paso a manual. Puro y sin React; los nombres siguen al JSON
 * del Core (snake_case).
 */

export const AGES = ["feudal_age", "castle_age", "imperial_age"] as const;
export type AgeTech = (typeof AGES)[number];

export interface AssistedGoal {
  age?: AgeTech;
  /** Con `age`: la población del clic (sin esto, lo antes posible). */
  at_pop?: number;
  unit?: string;
  count?: number;
  /** Cuántos edificios producen la unidad a la vez. */
  buildings?: number;
  town_centers?: number;
  tech?: string;
}

export interface AssistedOpening {
  houses: boolean;
  sheep: number;
  boar: boolean;
  berries: "auto" | "no";
}

export interface AssistedPlan {
  opening: AssistedOpening;
  villagers: { until: number };
  eco_upgrades: boolean;
  goals: AssistedGoal[];
}

/** Los rangos de la spec (sección 2), los mismos que revisa el validador del Core. */
export const LIMITS = { until: [1, 200], count: [1, 200], buildings: [1, 10], townCenters: [2, 10], atPop: [1, 200] } as const;

/** Una BO nueva en Asistido (spec, sección 6): aldeanos hasta 120, Feudal y Castillos. */
export function defaultPlan(): AssistedPlan {
  return {
    opening: { houses: true, sheep: 6, boar: true, berries: "auto" },
    villagers: { until: 120 },
    eco_upgrades: false,
    goals: [{ age: "feudal_age" }, { age: "castle_age" }],
  };
}

const clamp = (value: number, [min, max]: readonly [number, number]) => Math.min(max, Math.max(min, Math.round(value)));

/** El plan del JSON del Core, con los valores por defecto donde falta algo (lo que no se entiende se ignora). */
export function planFromJson(raw: unknown): AssistedPlan | null {
  if (typeof raw !== "object" || raw === null) return null;
  const r = raw as Record<string, unknown>;
  const base = defaultPlan();
  const opening = (typeof r.opening === "object" && r.opening !== null ? r.opening : {}) as Record<string, unknown>;
  const villagers = (typeof r.villagers === "object" && r.villagers !== null ? r.villagers : {}) as Record<string, unknown>;
  return {
    opening: {
      houses: typeof opening.houses === "boolean" ? opening.houses : base.opening.houses,
      sheep: typeof opening.sheep === "number" ? opening.sheep : base.opening.sheep,
      boar: typeof opening.boar === "boolean" ? opening.boar : base.opening.boar,
      berries: opening.berries === "no" ? "no" : "auto",
    },
    villagers: { until: typeof villagers.until === "number" ? villagers.until : base.villagers.until },
    eco_upgrades: r.eco_upgrades === true,
    goals: Array.isArray(r.goals) ? r.goals.filter((g): g is AssistedGoal => typeof g === "object" && g !== null) : [],
  };
}

export function setUntil(plan: AssistedPlan, until: number): AssistedPlan {
  return { ...plan, villagers: { until: clamp(until, LIMITS.until) } };
}

export function addGoal(plan: AssistedPlan, goal: AssistedGoal): AssistedPlan {
  return { ...plan, goals: [...plan.goals, goal] };
}

export function removeGoal(plan: AssistedPlan, index: number): AssistedPlan {
  return { ...plan, goals: plan.goals.filter((_, i) => i !== index) };
}

/** Mueve un objetivo de lugar (cambia su prioridad). */
export function moveGoal(plan: AssistedPlan, from: number, to: number): AssistedPlan {
  if (from === to || from < 0 || to < 0 || from >= plan.goals.length || to >= plan.goals.length) return plan;
  const goals = [...plan.goals];
  const [goal] = goals.splice(from, 1);
  goals.splice(to, 0, goal);
  return { ...plan, goals };
}

/** Cambia un objetivo, con las cantidades dentro de sus rangos. */
export function updateGoal(plan: AssistedPlan, index: number, patch: Partial<AssistedGoal>): AssistedPlan {
  const goals = plan.goals.map((goal, i) => {
    if (i !== index) return goal;
    const next: AssistedGoal = { ...goal, ...patch };
    if (next.count !== undefined) next.count = clamp(next.count, LIMITS.count);
    if (next.buildings !== undefined) next.buildings = clamp(next.buildings, LIMITS.buildings);
    if (next.town_centers !== undefined) next.town_centers = clamp(next.town_centers, LIMITS.townCenters);
    if (next.at_pop !== undefined) next.at_pop = clamp(next.at_pop, LIMITS.atPop);
    if ("at_pop" in patch && patch.at_pop === undefined) delete next.at_pop;
    return next;
  });
  return { ...plan, goals };
}

/** El nombre de un objetivo para la lista y la franja: "Edad de los Castillos", "20 exploradores en 2 establos". */
export function goalLabel(goal: AssistedGoal, buildingOf: (unit: string) => string | undefined = () => undefined): string {
  if (goal.age) return entityName(goal.age);
  if (goal.unit) {
    const count = goal.count ?? 1;
    const building = buildingOf(goal.unit);
    const where = building ? ` en ${goal.buildings ?? 1} ${entityName(building, goal.buildings ?? 1)}` : "";
    return `${count} ${entityName(goal.unit, count)}${where}`;
  }
  if (goal.town_centers) return `${goal.town_centers} centros urbanos`;
  if (goal.tech) return entityName(goal.tech);
  return "Objetivo";
}

export interface GoalShift {
  /** El lugar del objetivo en la lista nueva. */
  index: number;
  label: string;
  before: number | null;
  after: number | null;
  /** Segundos que se corrió (positivo: más tarde); null si antes o ahora no llega. */
  delta: number | null;
}

/** La franja "Último cambio" (spec, sección 6): qué se tocó y cómo se corrió cada objetivo. */
export interface ChangeStrip {
  change: string;
  shifts: GoalShift[];
  lines: string[];
}

/**
 * Compara la hora de los objetivos entre el análisis anterior y el nuevo (por lugar en la lista, los que siguen
 * siendo el mismo objetivo). Null sin análisis anterior.
 */
export function diffGoals(
  before: Analysis | null,
  after: Analysis,
  change: string,
  plan: AssistedPlan,
  previousPlan: AssistedPlan | null,
  buildingOf?: (unit: string) => string | undefined,
): ChangeStrip | null {
  if (!before?.assisted || !after.assisted || !previousPlan) return null;
  const shifts: GoalShift[] = [];
  const used = new Set<number>();
  plan.goals.forEach((goal, i) => {
    // El mismo objetivo en la lista anterior (aunque se haya movido), sin usar dos veces el mismo.
    const old = previousPlan.goals.findIndex((g, j) => !used.has(j) && sameKind(g, goal));
    if (old < 0) return;
    used.add(old);
    const b = before.assisted!.goals.find((g) => g.index === old)?.end ?? null;
    const a = after.assisted!.goals.find((g) => g.index === i)?.end ?? null;
    if (b === a) return;
    shifts.push({ index: i, label: goalLabel(goal, buildingOf), before: b, after: a, delta: a !== null && b !== null ? Math.round(a - b) : null });
  });
  const lines = shifts.map((s) => {
    const from = s.before === null ? "no llegaba" : formatClock(s.before);
    const to = s.after === null ? "no llega" : formatClock(s.after);
    const delta = s.delta === null ? "" : ` (${s.delta > 0 ? "+" : "−"}${formatClock(Math.abs(s.delta))})`;
    return `${s.label} ${from} a ${to}${delta}`;
  });
  return { change, shifts, lines };
}

/** Si dos objetivos son "el mismo" para comparar horas: misma edad, unidad, tecnología o centros urbanos. */
function sameKind(a: AssistedGoal, b: AssistedGoal) {
  return (a.age !== undefined && a.age === b.age) || (a.unit !== undefined && a.unit === b.unit)
    || (a.tech !== undefined && a.tech === b.tech) || (a.town_centers !== undefined && b.town_centers !== undefined);
}

/** "Pasar a manual" (spec, sección 2): la BO sin `assisted`, con los pasos generados para editarlos como siempre. */
export function toManual(analysis: Analysis): { bo: EditorBo; steps: EditorStep[] } {
  const { bo, steps } = fromAnalysis(analysis);
  delete bo.assisted;
  return { bo, steps };
}

/**
 * Junta los toques seguidos (spec, sección 6: "se espera a que dejen de llegar unos 150 ms"): cada llamada reemplaza
 * a la anterior y corre recién cuando pasan `ms` sin otra.
 */
export function debouncer(ms: number) {
  // setTimeout y clearTimeout se llaman desde globalThis (sueltos, el navegador da "Illegal invocation").
  const timers = { set: (fn: () => void, wait: number) => globalThis.setTimeout(fn, wait), clear: (id: ReturnType<typeof setTimeout>) => globalThis.clearTimeout(id) };
  let pending: ReturnType<typeof setTimeout> | null = null;
  return {
    schedule(fn: () => void) {
      if (pending !== null) timers.clear(pending);
      pending = timers.set(() => {
        pending = null;
        fn();
      }, ms);
    },
    cancel() {
      if (pending !== null) timers.clear(pending);
      pending = null;
    },
  };
}

/** El tiempo de espera de los toques seguidos. */
export const DEBOUNCE_MS = 150;
