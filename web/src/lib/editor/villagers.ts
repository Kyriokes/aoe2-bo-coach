import type { GameStart } from "../coach/types";
import { entityName, sourceName } from "../stepText";
import { insertBlock, snapDown, type EditorStep, type Source } from "./model";
import { placeNew, villagersAfter } from "./opening";
import { capitalize } from "./text";

/**
 * El editor "por aldeano" (D8, segunda parte): como en las guías, un edificio lo construye el próximo aldeano que sale
 * y la BO se lee por número de aldeano ("Aldeano 7: campamento maderero → madera").
 *
 * En el formato no hay nada nuevo: es un paso mixto con "1 aldeano → destino" y "construir X desde el destino, a N de
 * población", donde N es la población cuando sale ese aldeano. Como `at_pop` es un número fijo, el editor lo vuelve a
 * calcular cuando cambia lo que hay antes (`syncNewVillagerBuilds`), y lo reconoce al cargar una BO por la forma.
 */

/** Los aldeanos con que arranca la partida: los sin tarea y los que ya trabajan (algunas civs traen más). */
export function initialVillagers(start: GameStart) {
  return start.idle_villagers + Object.values(start.villagers).reduce<number>((sum, n) => sum + (n ?? 0), 0);
}

const isVillagerStep = (step: EditorStep | undefined): step is EditorStep => step !== undefined && step.do === "train" && step.what === "villager";

/** El número del primer y del último aldeano que entrena cada paso de aldeanos (con 3 iniciales, el primero que sale es el 4). */
export function villagerNumbers(steps: EditorStep[], start: GameStart): Map<string, { first: number; last: number }> {
  const numbers = new Map<string, { first: number; last: number }>();
  let n = initialVillagers(start);
  for (const step of steps) {
    if (!isVillagerStep(step)) continue;
    numbers.set(step.id, { first: n + 1, last: n + step.count });
    n += step.count;
  }
  return numbers;
}

/** La población cuando salieron las unidades de los primeros `count` pasos (con el explorador y los iniciales). */
export function populationAfter(steps: EditorStep[], count: number, start: GameStart) {
  let population = start.population;
  for (const step of steps.slice(0, count)) if (step.do === "train") population += step.count;
  return population;
}

/** La forma de un edificio que construye el próximo aldeano, sin mirar la población (que puede haber quedado vieja). */
function hasNewVillagerShape(steps: EditorStep[], index: number) {
  const step = steps[index];
  const villager = steps[index - 1];
  return (
    step !== undefined &&
    step.do === "build" &&
    step.what !== undefined &&
    step.what !== "farm" &&
    step.withPrevious &&
    step.count === 1 &&
    (step.builders ?? 1) === 1 &&
    step.at_pop !== undefined &&
    isVillagerStep(villager) &&
    villager.count === 1 &&
    villager.assign !== undefined &&
    step.from === villager.assign
  );
}

/** Si el paso es un edificio que construye el próximo aldeano (con la población justa). */
export function isBuiltByNewVillager(steps: EditorStep[], index: number, start: GameStart) {
  return hasNewVillagerShape(steps, index) && steps[index].at_pop === populationAfter(steps, index, start);
}

/** Los ids de los edificios que construye el próximo aldeano. */
export function newVillagerBuilds(steps: EditorStep[], start: GameStart): Set<string> {
  const ids = new Set<string>();
  steps.forEach((step, i) => {
    if (isBuiltByNewVillager(steps, i, start)) ids.add(step.id);
  });
  return ids;
}

/**
 * Después de una edición, la población de arranque de esos edificios (`ids`, los que lo eran antes) vuelve a ser la
 * de su aldeano: cambia si se agregaron o sacaron unidades antes, o con otra civ. Si la forma se rompió (se separó
 * del aldeano o cambió de dónde sale), se deja como está. Devuelve el mismo arreglo si no cambió nada.
 */
export function syncNewVillagerBuilds(steps: EditorStep[], ids: Set<string>, start: GameStart): EditorStep[] {
  let changed = false;
  const synced = steps.map((step, i) => {
    if (!ids.has(step.id) || !hasNewVillagerShape(steps, i)) return step;
    const population = populationAfter(steps, i, start);
    if (step.at_pop === population) return step;
    changed = true;
    return { ...step, at_pop: population };
  });
  return changed ? synced : steps;
}

/** Adónde va el aldeano que construye `what`: el recurso del edificio, o el del paso de aldeanos que sigue, o madera. */
export function builderDestination(what: string | undefined, next: EditorStep | undefined, available: readonly Source[]): Source {
  const usable = (s: Source | undefined): s is Source => s !== undefined && available.includes(s);
  const natural: Partial<Record<string, Source>> = { lumber_camp: "wood", mill: "berries", dock: "fish", mule_cart: "wood", settlement: "wood" };
  if (what === "mining_camp") return next?.assign === "stone" && usable("stone") ? "stone" : usable("gold") ? "gold" : "stone";
  if (what && usable(natural[what])) return natural[what]!;
  if (usable(next?.assign)) return next!.assign!;
  return usable("wood") ? "wood" : available[0] ?? "wood";
}

export interface Inserted {
  steps: EditorStep[];
  /** Los pasos nuevos (la caja que se arrastra) y el id del primero. */
  block: EditorStep[];
  blockId: string;
  /** El paso que conviene dejar elegido (el edificio). */
  selectId: string;
  /** Lo que se le cuenta al usuario, si el editor tomó una decisión que no se ve ("salió de los 4 de madera"). */
  note: string | null;
}

/**
 * Un edificio que construye el próximo aldeano, insertado en el índice `index` de `steps`: "1 aldeano → destino" y el
 * edificio, juntos. Si el paso de aldeanos que sigue va al mismo destino, ese aldeano sale de ahí (así "6 ovejas,
 * campamento, 4 madera" queda "6 ovejas, aldeano 7: campamento → madera, 3 madera"); si no, es un aldeano más.
 */
export function insertByNewVillager(
  steps: EditorStep[],
  build: EditorStep,
  index: number,
  start: GameStart,
  villagerId: string,
  available: readonly Source[],
): Inserted {
  const at = snapDown(steps, index);
  const before = steps.slice(0, at);
  let rest = steps.slice(at);
  const nextIndex = rest.findIndex((s) => isVillagerStep(s));
  const next = nextIndex >= 0 ? rest[nextIndex] : undefined;
  const destination = builderDestination(build.what, next, available);
  let note: string | null = null;

  const inChain = (i: number) => rest[i].withPrevious || (rest[i + 1]?.withPrevious ?? false);
  if (next && next.assign === destination && (next.count > 1 || !inChain(nextIndex))) {
    note = `sale de los ${next.count} que iban a ${sourceName(destination)}`;
    if (next.count > 1) {
      rest = rest.map((s, i) => (i === nextIndex ? { ...s, count: s.count - 1 } : s));
    } else {
      // Era un solo aldeano: pasa a ser el que construye (lo que dependía de ese paso, depende del nuevo).
      rest = rest.filter((_, i) => i !== nextIndex).map((s) => (s.after.includes(next.id) ? { ...s, after: s.after.map((a) => (a === next.id ? villagerId : a)) } : s));
    }
  }

  const villager: EditorStep = { id: villagerId, do: "train", what: "villager", count: 1, assign: destination, after: [], withPrevious: false };
  const withVillager = [...before, villager];
  const builder: EditorStep = {
    ...build,
    count: 1,
    from: destination,
    assign: destination,
    at_pop: populationAfter(withVillager, withVillager.length, start),
    withPrevious: true,
  };
  delete builder.builders;
  const block = [villager, builder];
  return { steps: [...before, ...block, ...rest], block, blockId: villagerId, selectId: builder.id, note };
}

/**
 * Un paso nuevo de la paleta en el índice `index`: un edificio (menos las granjas) lo construye el próximo aldeano si
 * en ese lugar no quedan aldeanos sin tarea; lo demás toma sus aldeanos de lo que hay ahí (`placeNew`).
 */
export function insertNewStep(steps: EditorStep[], step: EditorStep, index: number, start: GameStart, villagerId: string, available: readonly Source[]): Inserted {
  const at = snapDown(steps, index);
  const before = steps.slice(0, at);
  if (step.do === "build" && step.what !== "farm" && villagersAfter(before, start).idle === 0) {
    return insertByNewVillager(steps, step, at, start, villagerId, available);
  }
  const placed = placeNew(step, before, start);
  return { steps: insertBlock(steps, [placed], at), block: [placed], blockId: placed.id, selectId: placed.id, note: null };
}

/**
 * Deja de construirlo el próximo aldeano: el edificio sale de `from` (o de los sin tarea) y el aldeano vuelve al paso
 * de aldeanos que sigue si va al mismo lugar (deshace lo de `insertByNewVillager`); si no, queda como su propio paso.
 */
export function detachNewVillager(steps: EditorStep[], buildId: string, from: Source | undefined): EditorStep[] {
  const i = steps.findIndex((s) => s.id === buildId);
  if (i <= 0 || !hasNewVillagerShape(steps, i)) return steps;
  const build = steps[i];
  const villager = steps[i - 1];
  const detached: EditorStep = { ...build, withPrevious: false, from, assign: from ?? build.assign };
  delete detached.at_pop;
  const out = [...steps.slice(0, i - 1), detached, ...steps.slice(i + 1)];
  // El aldeano vuelve a su tanda: el primer paso de aldeanos después del edificio, si va al mismo lugar y no es de un grupo.
  const j = out.findIndex((s, k) => k >= i && isVillagerStep(s));
  if (j >= 0 && out[j].assign === villager.assign && !out[j].withPrevious) {
    out[j] = { ...out[j], count: out[j].count + 1 };
    return out.map((s) => (s.after.includes(villager.id) ? { ...s, after: s.after.map((a) => (a === villager.id ? out[j].id : a)) } : s));
  }
  return [...out.slice(0, i - 1), villager, ...out.slice(i - 1)];
}

/** Borrar un edificio que construye el próximo aldeano: también se va su aldeano (vuelve a su tanda si la hay). */
export function removeNewVillagerBuild(steps: EditorStep[], buildId: string): EditorStep[] | null {
  const i = steps.findIndex((s) => s.id === buildId);
  if (i <= 0 || !hasNewVillagerShape(steps, i)) return null;
  const detached = detachNewVillager(steps, buildId, undefined);
  return detached.filter((s) => s.id !== buildId).map((s) => (s.after.includes(buildId) ? { ...s, after: s.after.filter((a) => a !== buildId) } : s));
}

/** "Aldeanos 4 a 6", "Aldeano 7". */
function villagersText(n: { first: number; last: number }) {
  return n.first === n.last ? `Aldeano ${n.first}` : `Aldeanos ${n.first} a ${n.last}`;
}

/**
 * Los textos como en las guías, por paso: "Aldeanos 4 a 6 → ovejas" en los de aldeanos y "Aldeano 7: campamento
 * maderero → madera" en un edificio que construye el próximo aldeano. Los demás pasos no están (van con el texto del
 * Core).
 */
export function guideLabels(steps: EditorStep[], start: GameStart): Map<string, string> {
  const numbers = villagerNumbers(steps, start);
  const labels = new Map<string, string>();
  steps.forEach((step, i) => {
    const n = numbers.get(step.id);
    if (n && step.assign) labels.set(step.id, `${villagersText(n)} → ${sourceName(step.assign)}`);
    if (step.what && isBuiltByNewVillager(steps, i, start)) {
      const v = numbers.get(steps[i - 1].id)!;
      labels.set(step.id, `${villagersText(v)}: ${entityName(step.what, 1)}${step.assign ? ` → ${sourceName(step.assign)}` : ""}`);
    }
  });
  return labels;
}

/**
 * El texto de cada cadena de "junto con el anterior" para la lista: el aldeano que construye un edificio va en un solo
 * renglón ("Aldeano 7: campamento maderero → madera"); lo demás, cada paso con su texto y unidos por " + ".
 */
export function chainLabel(chain: EditorStep[], all: EditorStep[], labels: Map<string, string>, textOf: (step: EditorStep) => string) {
  const parts: string[] = [];
  chain.forEach((step, k) => {
    const next = chain[k + 1];
    // El aldeano del edificio que lo sigue ya está en el texto del edificio.
    if (next && isVillagerStep(step) && labels.get(next.id)?.startsWith("Aldeano") && hasNewVillagerShape(all, all.indexOf(next))) return;
    parts.push(labels.get(step.id) ?? capitalize(textOf(step)));
  });
  return parts.join(" + ");
}
