import { getDictionary } from "@/i18n";
import type { Locale } from "@/i18n/locales";
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
let mapsFile: Promise<string> | null = null;

export function loadGameData(): Promise<GameDataFile> {
  gameData ??= fetch(`${COACH_BASE}/game_data.json`)
    .then((r) => r.json() as Promise<GameDataFile>)
    .catch((error) => {
      gameData = null;
      throw error;
    });
  return gameData;
}

export function loadMaps(locale: Locale = "es"): Promise<MapInfo[]> {
  mapsFile ??= fetch(`${COACH_BASE}/maps.json`)
    .then((r) => (r.ok ? r.text() : ""))
    .catch((error) => {
      mapsFile = null;
      throw error;
    });
  return mapsFile.then((text) => parseMaps(text, locale));
}

/**
 * Lee maps.json (tiene comentarios de línea, como lo lee el Core). "any" va primero y siempre está. Cada mapa trae su
 * nombre y su nota en otro idioma ("name_en", "note_en"); si no, va el castellano.
 */
export function parseMaps(text: string, locale: Locale = "es"): MapInfo[] {
  const ANY_MAP: MapInfo = { key: "any", name: getDictionary(locale).editor.any, water: true, sources: [...SOURCES] };
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
    const localized = (field: string) => (typeof m[`${field}_${locale}`] === "string" ? m[`${field}_${locale}`] : m[field]);
    const [name, note] = [localized("name"), localized("note")];
    list.push({ key: key.trim(), name: typeof name === "string" ? name : key, water: m.water === true, sources, ...(typeof note === "string" ? { note } : {}) });
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
export function paletteItems(data: GameDataLite, civ: string, map: MapInfo | null = null, locale: Locale = "es"): PaletteGroup[] {
  const t = getDictionary(locale).editor.palette;
  const sources = map ? map.sources : [...SOURCES];
  const water = map ? map.water : true;
  const usable = (key: string) => {
    const entity = data.entities[key];
    if (!entity || !canMake(data, civ, key)) return false;
    if (!water && (key === "dock" || entity.made_in === "dock")) return false;
    return true;
  };
  const byAgeThenName = (a: string, b: string) =>
    AGE_ORDER.indexOf(data.entities[a].age) - AGE_ORDER.indexOf(data.entities[b].age) || entityName(a, 1, locale).localeCompare(entityName(b, 1, locale), locale);
  const keysOf = (kind: "unit" | "building" | "tech") => Object.keys(data.entities).filter((k) => data.entities[k].kind === kind && usable(k)).sort(byAgeThenName);

  const villagers: PaletteItem[] = sources.map((source) => ({
    key: `villagers:${source}`,
    label: paletteLabel("villagers", source, locale),
    kind: "train",
    age: "dark",
    icon: source === "farm" ? "farm" : `source_${source}`,
    step: { do: "train", what: "villager", count: 1, assign: source },
  }));
  const move: PaletteItem[] = [
    { key: "move", label: paletteLabel("move", undefined, locale), kind: "move", age: "dark", icon: "villager", step: { do: "move", count: 1, from: "sheep", assign: sources.includes("wood") ? "wood" : sources[0] } },
  ];
  const buildings: PaletteItem[] = keysOf("building").map((key) => ({
    key,
    label: paletteLabel("entity", key, locale),
    kind: "build",
    age: data.entities[key].age,
    icon: key,
    step: key === "farm" ? { do: "build", what: key, count: 1, from: "sheep", assign: "farm" } : { do: "build", what: key, count: 1, from: "wood", assign: "wood" },
  }));
  const techs = keysOf("tech").filter((k) => !AGE_TECHS.includes(k));
  const research: PaletteItem[] = [
    ...AGE_TECHS.filter(usable).map((key) => ({ key, label: paletteLabel("age", AGE_OF_TECH[key], locale), kind: "research" as const, age: data.entities[key].age, icon: key, step: { do: "research" as const, what: key, count: 1 } })),
    ...techs.map((key) => ({ key, label: capitalize(entityName(key, 1, locale)), kind: "research" as const, age: data.entities[key].age, icon: key, step: { do: "research" as const, what: key, count: 1 } })),
  ];
  const units: PaletteItem[] = keysOf("unit")
    .filter((k) => k !== "villager")
    .map((key) => ({ key, label: paletteLabel("entity", key, locale), kind: "train", age: data.entities[key].age, icon: key, step: { do: "train", what: key, count: 1 } }));

  return [
    { title: t.groupVillagers, items: villagers },
    { title: t.groupMove, items: move },
    { title: t.groupBuildings, items: buildings },
    { title: t.groupResearch, items: research },
    { title: t.groupUnits, items: units },
  ];
}
