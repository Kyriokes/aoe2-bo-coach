import type { GameStart } from "../coach/types";
import { isSource, SOURCES, type EditorStep, type Source } from "./model";

/**
 * El arranque de la BO en el editor (D8): qué hacen los aldeanos iniciales y de dónde sacan aldeanos las cajas nuevas.
 *
 * Al empezar la partida los 3 aldeanos iniciales no tienen tarea (los de más de algunas civs ya están en las ovejas)
 * y el simulador los deja quietos hasta que un paso los usa: un paso de construir o de mover sin "from". La apertura
 * es ese primer paso, que el editor arma según lo que se elija en la caja "Inicio".
 */

/** El arranque estándar (sin civ), por si todavía no hay análisis. */
export const STANDARD_START: GameStart = {
  food: 200,
  wood: 200,
  gold: 100,
  stone: 200,
  population: 4,
  population_cap: 5,
  idle_villagers: 3,
  villagers: {},
  houses: true,
};

export type OpeningKind = "houses" | "direct" | "custom";

export interface Opening {
  kind: OpeningKind;
  /** Adónde van los iniciales (después de las casas, o directo). */
  source: Source;
}

/** Lo que hacen los iniciales según el primer paso de la BO. */
export function detectOpening(steps: EditorStep[], start: GameStart): Opening {
  const first = steps[0];
  if (first && first.from === undefined && !first.withPrevious && !(steps[1]?.withPrevious ?? false)) {
    if (first.do === "build" && first.what === "house" && (first.builders ?? 1) === start.idle_villagers && first.assign) {
      return { kind: "houses", source: first.assign };
    }
    if (first.do === "move" && first.count === start.idle_villagers && first.assign) {
      return { kind: "direct", source: first.assign };
    }
  }
  return { kind: "custom", source: "sheep" };
}

/** El paso de apertura: 2 casas con todos los iniciales que después van a la fuente, o mandarlos directo. */
export function openingStep(opening: Opening, start: GameStart, id: string): EditorStep | null {
  if (opening.kind === "custom") return null;
  if (opening.kind === "houses" && start.houses) {
    return { id, do: "build", what: "house", count: 2, builders: start.idle_villagers, assign: opening.source, after: [], withPrevious: false };
  }
  return { id, do: "move", count: start.idle_villagers, assign: opening.source, after: [], withPrevious: false };
}

/**
 * Los pasos con otra apertura: reemplaza el paso de apertura que haya (conservando su id) o agrega uno al principio.
 * "custom" saca el paso de apertura y deja a los iniciales sin tarea hasta que otro paso los use.
 */
export function withOpening(steps: EditorStep[], opening: Opening, start: GameStart, newId: string): EditorStep[] {
  const current = detectOpening(steps, start);
  const rest = current.kind === "custom" ? steps : steps.slice(1);
  const step = openingStep(opening, start, current.kind === "custom" ? newId : steps[0].id);
  return step ? [step, ...rest] : rest;
}

/** Los aldeanos de cada fuente y los sin tarea después de los pasos dados (en unidades), como LivePlan.Layout. */
export function villagersAfter(steps: EditorStep[], start: GameStart): { idle: number; on: Record<Source, number> } {
  const on = Object.fromEntries(SOURCES.map((s) => [s, 0])) as Record<Source, number>;
  for (const [source, count] of Object.entries(start.villagers)) if (isSource(source)) on[source] = count;
  let idle = start.idle_villagers;
  const take = (from: Source | undefined, wanted: number) => {
    if (from) {
      const n = Math.min(wanted, on[from]);
      on[from] -= n;
      return n;
    }
    const n = Math.min(wanted, idle);
    idle -= n;
    return n;
  };
  const put = (to: Source | undefined, count: number) => {
    if (to) on[to] += count;
    else idle += count;
  };
  for (const step of steps) {
    if (step.do === "train" && step.what === "villager") put(step.assign, step.count);
    else if (step.do === "build" && step.what && (step.assign !== undefined || step.from === undefined)) {
      const builders = step.what === "farm" ? step.count : (step.builders ?? 1);
      put(step.assign ?? step.from, take(step.from, builders));
    } else if (step.do === "move") put(step.assign, take(step.from, step.count));
  }
  return { idle, on };
}

const FOOD: Source[] = ["sheep", "hunt", "berries", "fish", "farm"];

/** La fuente con más aldeanos entre las dadas (o null si no hay nadie). */
function busiest(on: Record<Source, number>, among: readonly Source[]): Source | null {
  let best: Source | null = null;
  for (const s of among) if (on[s] > 0 && (best === null || on[s] > on[best])) best = s;
  return best;
}

/**
 * Una caja nueva de la paleta en el lugar donde va: de dónde salen sus aldeanos según lo que hay en ese punto.
 * Construir: primero los sin tarea (y después vuelven a su recurso: madera, salvo las granjas), si no los de la
 * madera, si no la fuente con más gente. Mover: los sin tarea, si no la fuente de comida con más gente.
 */
export function placeNew(step: EditorStep, before: EditorStep[], start: GameStart): EditorStep {
  if (step.do !== "build" && step.do !== "move") return step;
  const { idle, on } = villagersAfter(before, start);
  const farm = step.do === "build" && step.what === "farm";
  let from: Source | undefined;
  if (idle > 0) from = undefined;
  else if (step.do === "move") from = busiest(on, FOOD) ?? busiest(on, SOURCES) ?? step.from;
  else if (farm) from = busiest(on, FOOD) ?? busiest(on, SOURCES) ?? step.from;
  else from = on.wood > 0 ? "wood" : (busiest(on, SOURCES) ?? step.from);
  let assign = step.assign;
  if (step.do === "build" && !farm) assign = from ?? "wood";
  if (step.do === "move" && assign === from) assign = from === "wood" ? "gold" : "wood";
  return { ...step, from, assign };
}
