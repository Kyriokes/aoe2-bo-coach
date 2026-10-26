import { CIV_NAMES } from "./civNames";

/**
 * Las estadísticas reales de cruces (data/matchup_stats.json, que escribe tools/stats; spec D10, 3.2): lo mismo que
 * lee el Core en C# (MatchupStats), para las páginas /cruces. Cada entrada (una pareja "a|b" en 1v1, con la chance de
 * la primera; una civ en equipos) tiene 180 casilleros [p, desde, hasta, n] en milésimas y partidas, en el orden mapa
 * (todos y los 4) × franja de Elo (todas y 5) × franja de duración (todas y 5). En las consultas, 0 es "todos" o
 * "todas" y 1..5 cada franja. Un archivo roto o de otra versión da null.
 */

/** Un casillero: la chance de ganar a Elo parejo (0 a 1), el rango probable del 90 % y las partidas detrás. */
export type Cell = { p: number; from: number; to: number; n: number };

/** La franja más corta y la más larga con partidas, cuando sus rangos no se tocan (regla de honestidad, 4.5). */
export type Trend = { shortBand: number; short: Cell; longBand: number; long: Cell; favorsShort: boolean };

export interface MatchupData {
  source: { name?: string; url?: string; from?: string; to?: string };
  patch: { label?: string; start?: string; previousMixed: boolean };
  matches: { oneVsOne: number; team: number };
  eloLimits: number[];
  minuteLimits: number[];
  mapKeys: string[];
  oneVsOne: Map<string, Int32Array>;
  team: Map<string, Int32Array>;
}

const VALUES = 5 * 6 * 6 * 4;
const SCHEMA_VERSION = 1;

/** Los nombres de los mapas de las estadísticas, los de data/maps.json (un test lo controla). */
export const MAP_NAMES: Readonly<Record<string, string>> = {
  arabia: "Arabia",
  arena: "Arena",
  black_forest: "Bosque Negro",
  nomad: "Nómada",
};

type Json = Record<string, unknown>;

function isObject(value: unknown): value is Json {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function text(value: unknown): string | undefined {
  return typeof value === "string" ? value : undefined;
}

function limits(value: unknown, fallback: number[]): number[] {
  if (!Array.isArray(value) || value.length !== 4 || !value.every((v) => Number.isInteger(v))) return fallback;
  return value.every((v, i) => i === 0 || v > value[i - 1]) ? (value as number[]) : fallback;
}

function entries(value: unknown, pairs: boolean): Map<string, Int32Array> {
  const out = new Map<string, Int32Array>();
  if (!isObject(value)) return out;
  for (const [rawKey, cells] of Object.entries(value)) {
    const key = rawKey.trim().toLowerCase();
    if (!key || (pairs && key.split("|").length !== 2)) continue;
    if (!Array.isArray(cells) || cells.length !== VALUES || !cells.every((v) => Number.isInteger(v))) continue;
    out.set(key, Int32Array.from(cells as number[]));
  }
  return out;
}

export function parseMatchups(json: string): MatchupData | null {
  let root: unknown;
  try {
    root = JSON.parse(json);
  } catch {
    return null;
  }
  if (!isObject(root)) return null;
  // Otra versión cambia el significado de algo que ya está: mejor sin datos que con números falsos.
  if ("schema_version" in root && root.schema_version !== SCHEMA_VERSION) return null;
  const oneVsOne = entries(root.one_vs_one, true);
  const team = entries(root.team, false);
  if (oneVsOne.size === 0 && team.size === 0) return null;
  const source = isObject(root.source) ? root.source : {};
  const patch = isObject(root.patch) ? root.patch : {};
  const matches = isObject(root.matches) ? root.matches : {};
  const bands = isObject(root.bands) ? root.bands : {};
  const maps = Array.isArray(bands.maps) && bands.maps.length === 4 && bands.maps.every((m) => typeof m === "string")
    ? (bands.maps as string[]).map((m) => m.trim().toLowerCase())
    : Object.keys(MAP_NAMES);
  return {
    source: { name: text(source.name), url: text(source.url), from: text(source.from), to: text(source.to) },
    patch: { label: text(patch.label), start: text(patch.start), previousMixed: patch.previous_mixed === true },
    matches: {
      oneVsOne: Number.isInteger(matches["1v1"]) ? (matches["1v1"] as number) : 0,
      team: Number.isInteger(matches.team) ? (matches.team as number) : 0,
    },
    eloLimits: limits(bands.elo, [850, 1050, 1300, 1600]),
    minuteLimits: limits(bands.game_minutes, [20, 30, 40, 55]),
    mapKeys: maps,
    oneVsOne,
    team,
  };
}

/** Los cuatro enteros de un casillero (milésimas y partidas), o null si el índice no existe. */
function raw(values: Int32Array, map: number, elo: number, duration: number): [number, number, number, number] | null {
  if (map < 0 || map > 4 || elo < 0 || elo > 5 || duration < 0 || duration > 5) return null;
  const i = ((map * 6 + elo) * 6 + duration) * 4;
  return [values[i], values[i + 1], values[i + 2], values[i + 3]];
}

function cell([p, from, to, n]: [number, number, number, number]): Cell {
  return { p: p / 1000, from: from / 1000, to: to / 1000, n };
}

/** La chance de que `civ` le gane a `rival` en 1v1, a Elo parejo; null en un espejo o si falta la pareja. */
export function oneVsOne(data: MatchupData, civ: string, rival: string, map: number, elo: number, duration: number): Cell | null {
  const a = civ.trim().toLowerCase();
  const b = rival.trim().toLowerCase();
  if (!a || !b || a === b) return null;
  const first = a < b;
  const values = data.oneVsOne.get(first ? `${a}|${b}` : `${b}|${a}`);
  const r = values ? raw(values, map, elo, duration) : null;
  if (!r) return null;
  // Al revés, en milésimas enteras: así 1 - 0,65 da 0,35 justo.
  return first ? cell(r) : cell([1000 - r[0], 1000 - r[2], 1000 - r[1], r[3]]);
}

/** La chance de que gane un equipo con esa civ en lugar de una civ promedio. En equipos, n son apariciones. */
export function team(data: MatchupData, civ: string, map: number, elo: number, duration: number): Cell | null {
  const values = data.team.get(civ.trim().toLowerCase());
  const r = values ? raw(values, map, elo, duration) : null;
  return r ? cell(r) : null;
}

/** La tendencia por duración (null si no hay dos franjas con partidas o si no se distinguen del ruido). */
export function trend(cellOf: (band: number) => Cell | null): Trend | null {
  const withGames = [1, 2, 3, 4, 5]
    .map((band) => ({ band, c: cellOf(band) }))
    .filter((x): x is { band: number; c: Cell } => x.c !== null && x.c.n > 0);
  if (withGames.length < 2) return null;
  const s = withGames[0];
  const l = withGames[withGames.length - 1];
  if (s.c.from <= l.c.to && l.c.from <= s.c.to) return null;
  return { shortBand: s.band, short: s.c, longBand: l.band, long: l.c, favorsShort: s.c.p > l.c.p };
}

/** Parejo: el rango incluye el 50 %. */
export function isEven(c: Cell): boolean {
  return c.from <= 0.5 && 0.5 <= c.to;
}

/** La clase del color de una chance: verde si el rango queda arriba del 50 %, rojo si queda abajo. */
export function chanceColor(c: Cell): string {
  if (c.from > 0.5) return "text-green";
  if (c.to < 0.5) return "text-red";
  return "text-muted";
}

export function percent(x: number): number {
  return Math.round(x * 100);
}

/** "49 % (46 a 53)". */
export function chanceText(c: Cell): string {
  return `${percent(c.p)} % (${percent(c.from)} a ${percent(c.to)})`;
}

/** La dirección de un cruce, con las claves en orden alfabético: "britons-vs-franks". */
export function pairSlug(a: string, b: string): string {
  const [x, y] = [a, b].sort();
  return `${x}-vs-${y}`;
}

/**
 * Qué página es un slug de /cruces: una civ (que tenga nombre en CIV_NAMES) o una pareja de civs distintas;
 * `canonical` es false si viene al revés del orden alfabético. null si no es ninguna.
 */
export function parseSlug(slug: string): { civ: string } | { a: string; b: string; canonical: boolean } | null {
  const parts = slug.trim().toLowerCase().split("-vs-");
  const known = (key: string) => Object.hasOwn(CIV_NAMES, key);
  if (parts.length === 1) return known(parts[0]) ? { civ: parts[0] } : null;
  if (parts.length !== 2) return null;
  const [first, second] = parts;
  if (!known(first) || !known(second) || first === second) return null;
  const [a, b] = [first, second].sort();
  return { a, b, canonical: first === a };
}

/** "todas", "menos de 850", "de 1050 a 1300", "1600 o más". */
export function eloLabel(data: MatchupData, band: number): string {
  const l = data.eloLimits;
  if (band <= 0) return "todas";
  if (band === 1) return `menos de ${l[0]}`;
  if (band > l.length) return `${l[l.length - 1]} o más`;
  return `de ${l[band - 2]} a ${l[band - 1]}`;
}

/** "cualquier duración", "menos de 20 min", "de 40 a 55 min", "55 min o más" (minutos del reloj del juego). */
export function durationLabel(data: MatchupData, band: number): string {
  const m = data.minuteLimits;
  if (band <= 0) return "cualquier duración";
  if (band === 1) return `menos de ${m[0]} min`;
  if (band > m.length) return `${m[m.length - 1]} min o más`;
  return `de ${m[band - 2]} a ${m[band - 1]} min`;
}
