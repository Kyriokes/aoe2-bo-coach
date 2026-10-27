import { entityName } from "../stepText";
import { AGE_OF_TECH, AGE_ORDER, AGE_UP_REQ, ageUpCount, allowedRange, provides, STARTING_BUILDING, type GameDataLite } from "./constraints";
import { paletteItems, type MapInfo, type PaletteItem } from "./gameData";
import { withArticle } from "./guard";
import type { EditorStep } from "./model";
import { AGE_LABEL, capitalize } from "./text";

/**
 * La paleta del editor como en el juego (D8, segunda vuelta, pedido del usuario del 2026-10-23): el aldeano junta y
 * construye, y cada edificio produce lo suyo. Tres filas: aldeanos (fuentes, mover y clics de edad), construir (todos
 * los edificios) y producir (una pestaña por edificio con lo que hace). En cada lugar de la BO se esconde lo que
 * todavía no se puede hacer ahí; el buscador lo encuentra igual y dice por qué no está.
 */

export interface ProducerTab {
  /** El edificio (clave de game_data). */
  key: string;
  label: string;
  items: PaletteItem[];
}

export interface PaletteBar {
  /** Aldeanos a cada fuente y mover. */
  villagers: PaletteItem[];
  /** Los clics de edad. */
  ages: PaletteItem[];
  /** Los edificios, por edad y con los de economía primero. */
  build: PaletteItem[];
  /** Una pestaña por edificio que produce algo (sin los aldeanos ni los clics de edad, que están arriba). */
  producers: ProducerTab[];
}

/** El orden de los edificios dentro de cada edad: economía primero, después lo militar, como el menú del aldeano. */
const BUILD_RANK = [
  "house",
  "farm",
  "mill",
  "lumber_camp",
  "mining_camp",
  "settlement",
  "mule_cart",
  "dock",
  "barracks",
  "palisade_wall",
  "outpost",
  "archery_range",
  "stable",
  "blacksmith",
  "market",
  "watch_tower",
  "stone_wall",
  "siege_workshop",
  "monastery",
  "university",
  "town_center",
  "castle",
];

/** El orden de las pestañas de "Producir en". */
const PRODUCER_ORDER = [
  STARTING_BUILDING,
  "mill",
  "lumber_camp",
  "mining_camp",
  "barracks",
  "archery_range",
  "stable",
  "blacksmith",
  "market",
  "siege_workshop",
  "monastery",
  "university",
  "dock",
  "castle",
];

const rank = (key: string) => {
  const i = BUILD_RANK.indexOf(key);
  return i < 0 ? BUILD_RANK.length : i;
};

/** Las filas de la paleta para una civ y un mapa (con el mismo filtro que `paletteItems`). */
export function paletteBar(data: GameDataLite, civ: string, map: MapInfo | null = null): PaletteBar {
  const items = paletteItems(data, civ, map).flatMap((g) => g.items);
  const villagers = items.filter((i) => i.key.startsWith("villagers:") || i.key === "move");
  const ages = items.filter((i) => i.kind === "research" && AGE_OF_TECH[i.key]);
  const build = items
    .filter((i) => i.kind === "build")
    .sort((a, b) => AGE_ORDER.indexOf(data.entities[a.key].age) - AGE_ORDER.indexOf(data.entities[b.key].age) || rank(a.key) - rank(b.key));
  // Un edificio tiene pestaña si la civ lo construye (o algo que vale por él: el asentamiento de los incas es molino).
  const built = new Set(build.flatMap((i) => [i.key, ...(data.entities[i.key]?.counts_as ?? [])]));
  const producers: ProducerTab[] = [];
  for (const key of PRODUCER_ORDER) {
    if (key !== STARTING_BUILDING && !built.has(key)) continue;
    const made = items.filter((i) => (i.kind === "train" || i.kind === "research") && !villagers.includes(i) && !ages.includes(i) && data.entities[i.key]?.made_in === key);
    if (made.length > 0) producers.push({ key, label: capitalize(entityName(key, 1)), items: made });
  }
  return { villagers, ages, build, producers };
}

/** Todas las cajas de la paleta, en el orden en que se ven. */
export function allItems(bar: PaletteBar): PaletteItem[] {
  return [...bar.villagers, ...bar.ages, ...bar.build, ...bar.producers.flatMap((t) => t.items)];
}

/**
 * Por qué una caja no se puede agregar en el índice de inserción `index` de la BO, o null si se puede. Son las reglas
 * de las cajas que se arrastran (edad, `after`, edificio que la produce, 2 edificios para subir de edad) más lo que el
 * juego no deja aunque el validador solo avise: investigar dos veces lo mismo, entrenar o investigar sin el edificio
 * antes, y subir de edad sin sus 2 edificios.
 */
export function unavailableHere(item: PaletteItem, steps: EditorStep[], index: number, data: GameDataLite): string | null {
  const step: EditorStep = { ...item.step, id: "\u0000paleta", after: [], withPrevious: false };
  if (step.do === "research" && steps.some((s) => s.do === "research" && s.what === step.what)) return "ya está en la BO";
  const range = allowedRange(steps, [step], data);
  if (range.lo > range.hi) return range.loReason ?? range.hiReason ?? "no hay lugar para ese paso";
  if (index < range.lo) return range.loReason;
  if (index > range.hi) return range.hiReason;

  const before = steps.slice(0, index);
  const has = (key: string) => key === STARTING_BUILDING || before.some((s) => provides(s, data).includes(key));
  const producer = step.what ? data.entities[step.what]?.made_in : undefined;
  // Si el edificio está más adelante, ya lo frenó el rango ("Recién después de construir..."): acá falta en toda la BO.
  if (step.do !== "build" && producer && !has(producer)) return `falta ${withArticle(producer)} en la BO`;
  const req = step.do === "research" && step.what ? AGE_UP_REQ[step.what] : undefined;
  if (req && ageUpCount(req, new Set(req.buildings.filter(has))) < 2) {
    return `faltan 2 edificios de ${AGE_LABEL[req.requires]} antes (${req.buildings.map((b) => entityName(b, 1)).join(", ")})`;
  }
  return null;
}

export interface PaletteHere {
  /** La paleta con lo que se puede agregar ahí (sin las pestañas que quedan vacías). */
  bar: PaletteBar;
  /** El motivo de cada caja escondida, por su clave. */
  hidden: Map<string, string>;
}

/** La paleta en un lugar de la BO: esconde lo que no se puede agregar ahí y guarda el motivo. */
export function paletteHere(bar: PaletteBar, steps: EditorStep[], index: number, data: GameDataLite): PaletteHere {
  const hidden = new Map<string, string>();
  const keep = (list: PaletteItem[]) =>
    list.filter((item) => {
      const reason = unavailableHere(item, steps, index, data);
      if (reason) hidden.set(item.key, reason);
      return reason === null;
    });
  return {
    bar: {
      villagers: keep(bar.villagers),
      ages: keep(bar.ages),
      build: keep(bar.build),
      producers: bar.producers.map((t) => ({ ...t, items: keep(t.items) })).filter((t) => t.items.length > 0),
    },
    hidden,
  };
}

/** Sin tildes y en minúsculas, para buscar ("galeria" encuentra "Galería de tiro"). */
export function normalize(text: string) {
  return text.normalize("NFD").replace(/\p{Diacritic}/gu, "").toLowerCase().trim();
}

/** Las cajas cuyo nombre tiene todas las palabras buscadas (al principio de alguna palabra del nombre). */
export function searchPalette(items: PaletteItem[], query: string): PaletteItem[] {
  const words = normalize(query).split(/\s+/).filter(Boolean);
  if (words.length === 0) return [];
  return items.filter((item) => {
    const name = normalize(item.label).split(/[\s,.()]+/);
    return words.every((w) => name.some((part) => part.startsWith(w)));
  });
}
