import { COACH_BASE } from "../coach/browser";
import { entityName } from "../stepText";
import { AGE_OF_TECH, AGE_ORDER, canMake, type GameDataLite } from "./constraints";
import { SOURCES, type EditorStep, type Source, type StepKind } from "./model";
import { capitalize, paletteLabel } from "./text";

/** Los datos del juego y los mapas que carga el editor en el navegador (los mismos archivos que usa el Core). */

export interface GameDataFile extends GameDataLite {
  dat_version?: string;
  civs: Record<string, { unavailable?: string[]; names?: Record<string, string> }>;
}

export interface MapInfo {
  key: string;
  name: string;
  water: boolean;
  sources: Source[];
  note?: string;
}

let gameData: Promise<GameDataFile> | null = null;
let maps: Promise<MapInfo[]> | null = null;

export function loadGameData(): Promise<GameDataFile> {
  gameData ??= fetch(`${COACH_BASE}/game_data.json`)
    .then((r) => r.json() as Promise<GameDataFile>)
    .catch((error) => {
      gameData = null;
      throw error;
    });
  return gameData;
}

export function loadMaps(): Promise<MapInfo[]> {
  maps ??= fetch(`${COACH_BASE}/maps.json`)
    .then((r) => (r.ok ? r.text() : ""))
    .then(parseMaps)
    .catch((error) => {
      maps = null;
      throw error;
    });
  return maps;
}

const ANY_MAP: MapInfo = { key: "any", name: "Cualquiera", water: true, sources: [...SOURCES] };

/** Lee maps.json (tiene comentarios de línea, como lo lee el Core). "any" va primero y siempre está. */
export function parseMaps(text: string): MapInfo[] {
  const json = text.replace(/^\s*\/\/.*$/gm, "");
  let root: { maps?: Record<string, unknown> } = {};
  try {
    root = json.trim() ? (JSON.parse(json) as typeof root) : {};
  } catch {
    return [ANY_MAP];
  }
  const list: MapInfo[] = [];
  for (const [key, value] of Object.entries(root.maps ?? {})) {
    if (typeof value !== "object" || value === null || !key.trim()) continue;
    const m = value as Record<string, unknown>;
    const sources = Array.isArray(m.sources) ? (m.sources.filter((s) => (SOURCES as readonly unknown[]).includes(s)) as Source[]) : [...SOURCES];
    list.push({ key: key.trim(), name: typeof m.name === "string" ? m.name : key, water: m.water === true, sources, ...(typeof m.note === "string" ? { note: m.note } : {}) });
  }
  const any = list.find((m) => m.key === "any");
  return [any ? { ...any, water: true, sources: [...SOURCES] } : ANY_MAP, ...list.filter((m) => m.key !== "any")];
}

/** Una caja de la paleta: lo que se inserta (sin id ni "junto con el anterior"). */
export interface PaletteItem {
  key: string;
  label: string;
  kind: StepKind;
  age: string;
  /** El ícono (archivo de /icons sin la extensión). */
  icon: string;
  step: Pick<EditorStep, "do" | "what" | "count" | "assign" | "from">;
}

export interface PaletteGroup {
  title: string;
  items: PaletteItem[];
}

const AGE_TECHS = Object.keys(AGE_OF_TECH);

/**
 * Las cajas de la paleta para una civ y un mapa: aldeanos a cada fuente que tenga el mapa, mover aldeanos,
 * edificios, investigaciones (los clics de edad primero) y unidades; sin lo que la civ no puede hacer ni, sin agua,
 * el muelle y lo que sale de él.
 */
export function paletteItems(data: GameDataLite, civ: string, map: MapInfo | null = null): PaletteGroup[] {
  const sources = map ? map.sources : [...SOURCES];
  const water = map ? map.water : true;
  const usable = (key: string) => {
    const entity = data.entities[key];
    if (!entity || !canMake(data, civ, key)) return false;
    if (!water && (key === "dock" || entity.made_in === "dock")) return false;
    return true;
  };
  const byAgeThenName = (a: string, b: string) =>
    AGE_ORDER.indexOf(data.entities[a].age) - AGE_ORDER.indexOf(data.entities[b].age) || entityName(a, 1).localeCompare(entityName(b, 1), "es");
  const keysOf = (kind: "unit" | "building" | "tech") => Object.keys(data.entities).filter((k) => data.entities[k].kind === kind && usable(k)).sort(byAgeThenName);

  const villagers: PaletteItem[] = sources.map((source) => ({
    key: `villagers:${source}`,
    label: paletteLabel("villagers", source),
    kind: "train",
    age: "dark",
    icon: source === "farm" ? "farm" : `source_${source}`,
    step: { do: "train", what: "villager", count: 1, assign: source },
  }));
  const move: PaletteItem[] = [
    { key: "move", label: paletteLabel("move"), kind: "move", age: "dark", icon: "villager", step: { do: "move", count: 1, from: "sheep", assign: sources.includes("wood") ? "wood" : sources[0] } },
  ];
  const buildings: PaletteItem[] = keysOf("building").map((key) => ({
    key,
    label: paletteLabel("entity", key),
    kind: "build",
    age: data.entities[key].age,
    icon: key,
    step: key === "farm" ? { do: "build", what: key, count: 1, from: "sheep", assign: "farm" } : { do: "build", what: key, count: 1, from: "wood", assign: "wood" },
  }));
  const techs = keysOf("tech").filter((k) => !AGE_TECHS.includes(k));
  const research: PaletteItem[] = [
    ...AGE_TECHS.filter(usable).map((key) => ({ key, label: paletteLabel("age", AGE_OF_TECH[key]), kind: "research" as const, age: data.entities[key].age, icon: key, step: { do: "research" as const, what: key, count: 1 } })),
    ...techs.map((key) => ({ key, label: capitalize(entityName(key, 1)), kind: "research" as const, age: data.entities[key].age, icon: key, step: { do: "research" as const, what: key, count: 1 } })),
  ];
  const units: PaletteItem[] = keysOf("unit")
    .filter((k) => k !== "villager")
    .map((key) => ({ key, label: paletteLabel("entity", key), kind: "train", age: data.entities[key].age, icon: key, step: { do: "train", what: key, count: 1 } }));

  return [
    { title: "Aldeanos", items: villagers },
    { title: "Mover", items: move },
    { title: "Edificios", items: buildings },
    { title: "Investigaciones", items: research },
    { title: "Unidades", items: units },
  ];
}

/** Una columna del árbol: un edificio (que se construye tocándolo) y lo que se hace en él, por edad. */
export interface TreeColumn {
  key: string;
  label: string;
  /** La caja para construir el edificio (null en "Edades" y "Aldeanos"). */
  building: PaletteItem | null;
  /** Lo que se hace en el edificio, por edad (dark, feudal, castle, imperial). */
  byAge: Record<string, PaletteItem[]>;
}

/** El orden de las columnas, como el árbol del juego: economía primero, después lo militar. */
const COLUMN_ORDER = [
  "town_center",
  "mill",
  "lumber_camp",
  "mining_camp",
  "market",
  "blacksmith",
  "university",
  "barracks",
  "archery_range",
  "stable",
  "siege_workshop",
  "monastery",
  "dock",
  "castle",
];

/**
 * La paleta como el árbol de tecnologías del juego (D8): una columna "Edades" con los clics, una "Aldeanos" con las
 * fuentes y mover, una por edificio con lo que produce (unidades e investigaciones) en la fila de su edad, y "Otros
 * edificios" con lo que no produce (casas, granjas, muros, torres...). Con el mismo filtro de civ y mapa que la lista.
 */
export function paletteTree(data: GameDataLite, civ: string, map: MapInfo | null = null): TreeColumn[] {
  const groups = paletteItems(data, civ, map);
  const items = groups.flatMap((g) => g.items);
  const empty = () => Object.fromEntries(AGE_ORDER.map((a) => [a, [] as PaletteItem[]]));
  const columns = new Map<string, TreeColumn>();
  const column = (key: string, building: PaletteItem | null) => {
    let c = columns.get(key);
    if (!c) {
      c = { key, label: capitalize(entityName(key, 1)), building, byAge: empty() };
      columns.set(key, c);
    }
    return c;
  };

  const ages: TreeColumn = { key: "ages", label: "Edades", building: null, byAge: empty() };
  const villagers: TreeColumn = { key: "villagers", label: "Aldeanos", building: null, byAge: empty() };
  const others: TreeColumn = { key: "others", label: "Otros edificios", building: null, byAge: empty() };
  for (const item of items) {
    if (item.key.startsWith("villagers:") || item.key === "move") villagers.byAge.dark.push(item);
    else if (item.kind === "research" && AGE_OF_TECH[item.key]) ages.byAge[item.age].push(item);
  }
  for (const key of COLUMN_ORDER) {
    const building = items.find((i) => i.kind === "build" && i.key === key) ?? null;
    if (building || key === "town_center") column(key, building);
  }
  for (const item of items) {
    if (item.kind === "build") {
      if (!columns.has(item.key)) others.byAge[item.age].push(item);
      continue;
    }
    if (item.key.startsWith("villagers:") || item.key === "move" || AGE_OF_TECH[item.key]) continue;
    const producer = data.entities[item.key]?.made_in;
    if (producer && columns.has(producer)) columns.get(producer)!.byAge[item.age].push(item);
  }
  const ordered = COLUMN_ORDER.flatMap((k) => (columns.has(k) ? [columns.get(k)!] : []));
  // Columnas que no tienen nada que hacer en esta civ (la universidad sin tecnologías cargadas) quedan solo con el edificio.
  return [ages, villagers, ...ordered, others];
}
