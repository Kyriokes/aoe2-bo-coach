import { fill, getDictionary } from "@/i18n";
import type { Locale } from "@/i18n/locales";
import { entityName } from "../stepText";
import { isBoundary, type EditorStep } from "./model";
import { AGE_LABEL, ageLabel } from "./text";

/**
 * Dónde puede caer una caja al arrastrarla (D8): las reglas del juego que el validador del Core conoce, para frenar
 * la caja antes de simular. Lo que el simulador decide (recursos, población, aldeanos) se ve en la vista previa:
 * la caja se pega al tiempo que da la simulación.
 *
 * Las constantes están copiadas de BuildOrderValidator (AgeTechs, AgeUpRequirements, StartingBuilding): hay que
 * cambiarlas en los dos lados. Un test con el Core en WebAssembly controla que coincidan en cada movimiento de las
 * BO de ejemplo.
 */

export type Age = keyof typeof AGE_LABEL;

export const AGE_ORDER: readonly Age[] = ["dark", "feudal", "castle", "imperial"];

/** La edad a la que lleva cada tecnología de edad. */
export const AGE_OF_TECH: Readonly<Record<string, Age>> = { feudal_age: "feudal", castle_age: "castle", imperial_age: "imperial" };

/** Los edificios de la edad anterior que pide cada edad (2 de ellos). */
export const AGE_UP_REQ: Readonly<Record<string, { requires: Age; buildings: readonly string[]; alone?: string }>> = {
  feudal_age: { requires: "dark", buildings: ["barracks", "mill", "lumber_camp", "mining_camp", "dock"] },
  castle_age: { requires: "feudal", buildings: ["archery_range", "stable", "blacksmith", "market"] },
  // D12: 2 edificios de Castillos o un castillo solo (cuenta por los dos), como BuildOrderValidator.AgeUpProgress.
  imperial_age: { requires: "castle", buildings: ["university", "monastery", "siege_workshop", "castle"], alone: "castle" },
};

/** Cuántos de los 2 edificios que pide la edad hay en `keys` (el edificio `alone` cuenta por dos). */
export function ageUpCount(req: { buildings: readonly string[]; alone?: string }, keys: ReadonlySet<string>): number {
  const count = req.buildings.filter((b) => keys.has(b)).length + (req.alone && keys.has(req.alone) ? 1 : 0);
  return Math.min(2, count);
}

export const STARTING_BUILDING = "town_center";

export interface EntityLite {
  kind: "unit" | "building" | "tech";
  age: Age;
  made_in?: string;
  counts_as?: string[];
  /** El edificio que tiene que estar antes para poder construir este (la galería, el cuartel). */
  requires?: string;
  cost?: Partial<Record<"food" | "wood" | "gold" | "stone", number>>;
}

/** Lo que el editor necesita de game_data.json. */
export interface GameDataLite {
  entities: Record<string, EntityLite>;
  civs?: Record<string, { unavailable?: string[]; no_requires?: string[]; no_age_buildings?: boolean; ages?: Record<string, Age> }>;
}

/** Índices de inserción entre los demás pasos donde puede caer la caja; `lo > hi` si no puede ir a ningún lado. */
export interface Range {
  lo: number;
  hi: number;
  loReason: string | null;
  hiReason: string | null;
}

const ageIndex = (age: Age | undefined) => (age ? AGE_ORDER.indexOf(age) : 0);

/** Si la civ puede hacer esa entidad (como GameData.CanMake): no está en `unavailable` ni se hace en algo que lo esté. */
export function canMake(data: GameDataLite, civ: string, key: string) {
  const unavailable = data.civs?.[civ.toLowerCase()]?.unavailable;
  if (!unavailable || unavailable.length === 0) return true;
  if (unavailable.includes(key)) return false;
  const madeIn = data.entities[key]?.made_in;
  return madeIn === undefined || !unavailable.includes(madeIn);
}

/** El edificio que pide antes `key` con esa civ (como CivBonuses.Requires): los jemeres no piden ninguno. */
export function requiresOf(data: GameDataLite, civ: string, key: string) {
  const requires = data.entities[key]?.requires;
  return requires && !data.civs?.[civ.toLowerCase()]?.no_requires?.includes(key) ? requires : undefined;
}

/** La edad desde la que la civ hace `key` (como CivBonuses.AgeOf): los cumanos, el centro urbano en Feudal. */
export function ageOf(data: GameDataLite, civ: string, key: string): Age | undefined {
  return data.civs?.[civ.toLowerCase()]?.ages?.[key] ?? data.entities[key]?.age;
}

/** Si la civ sube de edad sin los 2 edificios (como CivBonuses.NoAgeBuildings): los jemeres. */
export function skipsAgeBuildings(data: GameDataLite, civ: string) {
  return data.civs?.[civ.toLowerCase()]?.no_age_buildings === true;
}

/** Lo que construye un paso de construir: el edificio y lo que vale por otros (asentamiento, carro de mulas). */
export function provides(step: EditorStep, data: GameDataLite): string[] {
  if (step.do !== "build" || !step.what) return [];
  return [step.what, ...(data.entities[step.what]?.counts_as ?? [])];
}

/** El primer borde de cadena en `index` o después. */
export function snapUp(steps: EditorStep[], index: number) {
  let p = Math.max(0, index);
  while (p < steps.length && !isBoundary(steps, p)) p++;
  return p;
}

/** El primer borde de cadena en `index` o antes. */
export function snapDownTo(steps: EditorStep[], index: number) {
  let p = Math.min(index, steps.length);
  while (p > 0 && !isBoundary(steps, p)) p--;
  return p;
}

const defaultLabel = (step: EditorStep, locale: Locale) => {
  const t = getDictionary(locale).editor.rules;
  return step.what ? entityName(step.what, step.count, locale) : step.do === "move" ? t.moveVillagers : t.thatStep;
};

/**
 * El rango de índices de inserción (entre `others`, los pasos sin la caja) donde puede caer la caja `block` (un paso
 * o una cadena de "junto con el anterior"), con el motivo del límite de cada lado. `label` es el texto de un paso
 * para los motivos ("Antes de Entrenar 5 exploradores").
 */
export function allowedRange(others: EditorStep[], block: EditorStep[], data: GameDataLite, labelOf?: (step: EditorStep) => string, locale: Locale = "es", civ = "any"): Range {
  const t = getDictionary(locale).editor.rules;
  const label = labelOf ?? ((step: EditorStep) => defaultLabel(step, locale));
  const name = (key: string) => entityName(key, 1, locale);
  const n = others.length;
  const civAge = (key: string) => ageOf(data, civ, key);
  const freeAgeUp = skipsAgeBuildings(data, civ);
  let lo = 0;
  let hi = n;
  let loReason: string | null = null;
  let hiReason: string | null = null;
  const raise = (p: number, reason: string) => {
    if (p > lo) {
      lo = p;
      loReason = reason;
    }
  };
  const lower = (p: number, reason: string) => {
    if (p < hi) {
      hi = p;
      hiReason = reason;
    }
  };

  // La edad y los edificios que hay antes de cada índice de inserción (sin la caja).
  const ages: Age[] = ["dark"];
  const built: Set<string>[] = [new Set([STARTING_BUILDING])];
  for (let i = 0; i < n; i++) {
    const step = others[i];
    const age = step.do === "research" && step.what ? AGE_OF_TECH[step.what] : undefined;
    ages.push(age && ageIndex(age) > ageIndex(ages[i]) ? age : ages[i]);
    const next = new Set(built[i]);
    for (const key of provides(step, data)) next.add(key);
    built.push(next);
  }
  const firstAgeIndexAtLeast = (age: Age) => ages.findIndex((a) => ageIndex(a) >= ageIndex(age));

  // Lo que la propia caja aporta antes de cada miembro (un grupo "clic en Feudal + cuartel" se mira en orden).
  let blockAge: Age = "dark";
  const blockBuilt = new Set<string>();
  const blockIds = new Set(block.map((s) => s.id));

  for (const member of block) {
    const entity = member.what ? data.entities[member.what] : undefined;

    // (a) Una entidad va después del clic de su edad.
    const entityAge = member.what ? civAge(member.what) : undefined;
    if (entity && entityAge && member.do !== "move" && ageIndex(entityAge) > 0 && ageIndex(blockAge) < ageIndex(entityAge)) {
      const p = firstAgeIndexAtLeast(entityAge);
      raise(p < 0 ? n + 1 : p, fill(t.afterClick, { age: ageLabel(entityAge, locale) }));
    }

    // (b) Un clic de edad va antes del primer paso que necesita esa edad.
    if (member.do === "research" && member.what && AGE_OF_TECH[member.what]) {
      const reached = AGE_OF_TECH[member.what];
      const j = others.findIndex((s) => s.do !== "move" && s.what && ageIndex(civAge(s.what)) >= ageIndex(reached));
      if (j >= 0) lower(j, fill(t.beforeNeeds, { step: label(others[j]), age: ageLabel(reached, locale) }));
    }

    // (c) Después de los pasos de los que depende, y antes de los que dependen de él.
    for (const dep of member.after) {
      if (blockIds.has(dep)) continue;
      const j = others.findIndex((s) => s.id === dep);
      if (j >= 0) raise(j + 1, fill(t.afterDependency, { step: label(others[j]) }));
    }
    const dependent = others.findIndex((s) => s.after.includes(member.id));
    if (dependent >= 0) lower(dependent, fill(t.beforeDependent, { step: label(others[dependent]) }));

    // (d) Lo que se entrena o investiga va después del edificio que lo produce.
    const producer = entity?.made_in;
    if ((member.do === "train" || member.do === "research") && producer && producer !== STARTING_BUILDING && !blockBuilt.has(producer)) {
      const f = others.findIndex((s) => provides(s, data).includes(producer));
      if (f >= 0) raise(f + 1, fill(t.afterBuilding, { building: name(producer) }));
    }

    // (d) Un edificio no pasa después de lo que lo necesita si es el único que hay antes (el centro urbano inicial siempre está).
    for (const key of provides(member, data)) {
      if (key === STARTING_BUILDING) continue;
      const f = others.findIndex((s) => provides(s, data).includes(key));
      const j = others.findIndex((s) => (s.do === "train" || s.do === "research") && s.what && data.entities[s.what]?.made_in === key);
      if (j >= 0 && (f < 0 || f > j)) lower(j, fill(t.beforeOnly, { step: label(others[j]), building: name(key) }));
    }

    // (e) El clic de edad va después del segundo edificio que califica.
    const req = member.do === "research" && member.what && !freeAgeUp ? AGE_UP_REQ[member.what] : undefined;
    if (req) {
      const fromBlock = req.buildings.filter((b) => blockBuilt.has(b));
      const p = built.findIndex((set) => ageUpCount(req, new Set([...req.buildings.filter((b) => set.has(b)), ...fromBlock])) >= 2);
      if (p >= 0) {
        const have = req.buildings.filter((b) => built[Math.min(p, n)].has(b) || fromBlock.includes(b));
        raise(p, fill(t.afterTwo, { age: ageLabel(req.requires, locale), buildings: have.map(name).join(", ") }));
      } else {
        // Sin 2 en ninguna parte de la BO no hay lugar: el juego no deja hacer clic (y el simulador lo espera para siempre).
        raise(n + 1, fill(t.missingTwo, { age: ageLabel(req.requires, locale), buildings: req.buildings.filter((b) => b !== "dock").map(name).join(", ") }));
      }
    }

    // (e) Un edificio que califica no pasa después del clic si lo deja con menos de 2.
    for (const key of freeAgeUp ? [] : provides(member, data)) {
      for (const [tech, r] of Object.entries(AGE_UP_REQ)) {
        if (!r.buildings.includes(key)) continue;
        others.forEach((s, j) => {
          if (s.do !== "research" || s.what !== tech) return;
          const before = r.buildings.filter((b) => built[j].has(b));
          if (ageUpCount(r, new Set(before)) < 2 && ageUpCount(r, new Set([...before, key])) >= 2) {
            lower(j, fill(t.beforeAgeClick, { age: ageLabel(AGE_OF_TECH[tech], locale), required: ageLabel(r.requires, locale) }));
          }
        });
      }
    }

    if (member.do === "research" && member.what && AGE_OF_TECH[member.what] && ageIndex(AGE_OF_TECH[member.what]) > ageIndex(blockAge)) {
      blockAge = AGE_OF_TECH[member.what];
    }
    for (const key of provides(member, data)) blockBuilt.add(key);
  }

  // (f) Los grupos van juntos: los límites se corren al borde de cadena más cercano.
  lo = snapUp(others, lo);
  hi = snapDownTo(others, hi);
  return { lo, hi, loReason, hiReason };
}

export interface Clamped {
  index: number;
  /** El motivo si el índice pedido quedó afuera del rango, o null. */
  reason: string | null;
}

/**
 * El índice de inserción que corresponde a un instante: cuántos pasos arrancan antes. Los `start` que da el
 * simulador no son monótonos (entrenar "arranca" al encolarse), así que se toma el máximo acumulado, y un paso sin
 * tiempo (null) vale infinito para él y los que siguen.
 */
export function indexFromTime(t: number, starts: (number | null)[]): number {
  let running = -Infinity;
  let count = 0;
  for (const start of starts) {
    running = start === null ? Infinity : Math.max(running, start);
    if (running < t) count++;
    else break;
  }
  return count;
}

/** Lleva un índice de inserción que cae dentro de una cadena al borde más cercano según el instante. */
export function snapToBoundary(index: number, others: EditorStep[], t: number, starts: (number | null)[], ends: (number | null)[]): number {
  if (isBoundary(others, index)) return index;
  let g0 = index;
  while (g0 > 0 && others[g0].withPrevious) g0--;
  let g1 = index;
  while (g1 + 1 < others.length && others[g1 + 1].withPrevious) g1++;
  const start = starts[g0];
  const end = ends[g1];
  if (start === null || end === null || start === undefined || end === undefined) return g0;
  return t < (start + end) / 2 ? g0 : g1 + 1;
}

/** Deja el índice dentro del rango y dice por qué si lo corrió. Con `lo > hi` (no se puede mover) devuelve `pinned`. */
export function clampCandidate(raw: number, range: Range, pinned: number): Clamped {
  if (range.lo > range.hi) return { index: pinned, reason: range.loReason ?? range.hiReason };
  if (raw < range.lo) return { index: range.lo, reason: range.loReason };
  if (raw > range.hi) return { index: range.hi, reason: range.hiReason };
  return { index: raw, reason: null };
}
