import { fill, getDictionary } from "@/i18n";
import type { Locale } from "@/i18n/locales";
import { civNames } from "./civNames";
import { mapName, oneVsOne, pairSlug, parseSlug, percent, team, trend, type Cell, type MatchupData } from "./matchups";

/**
 * Lo que necesita cada página de /cruces, sacado de las estadísticas: solo los casilleros de esa página, así el
 * navegador no recibe el archivo entero (3,7 MB) para cambiar el mapa o la franja de Elo.
 */

/** Las civs que tienen estadísticas, en orden alfabético de su clave. */
export function civsWithData(data: MatchupData): string[] {
  const civs = new Set<string>(data.team.keys());
  for (const pair of data.oneVsOne.keys()) for (const civ of pair.split("|")) civs.add(civ);
  return [...civs].sort();
}

/** Las rutas de /cruces para el sitemap: la de elegir, la de cada civ y la de cada pareja (claves en orden alfabético). */
export function matchupSitemapPaths(data: MatchupData): string[] {
  return ["/cruces", ...civsWithData(data).map((civ) => `/cruces/${civ}`), ...[...data.oneVsOne.keys()].sort().map((pair) => `/cruces/${pair.replace("|", "-vs-")}`)];
}

/**
 * Las páginas de /cruces/[slug] que se arman al compilar: cada civ y cada pareja (claves en orden alfabético). Sin
 * datos legibles, una sola (los francos), que dice que no hay estadísticas: con Cache Components, una lista vacía
 * rompe el build.
 */
export function matchupStaticParams(data: MatchupData | null): { slug: string }[] {
  if (!data) return [{ slug: "franks" }];
  return [...civsWithData(data).map((civ) => ({ slug: civ })), ...[...data.oneVsOne.keys()].map((pair) => ({ slug: pair.replace("|", "-vs-") }))];
}

/**
 * Título, descripción y dirección de una página de /cruces/[slug] ("Britanos contra francos: estadísticas del
 * cruce"); null si el slug no es una civ ni una pareja en orden alfabético. Las civs van en plural y con artículo:
 * "les ganan los britanos a los francos".
 */
export function matchupPageMeta(slug: string, locale: Locale = "es"): { title: string; description: string; path: string } | null {
  const parsed = parseSlug(slug);
  if (!parsed) return null;
  const t = getDictionary(locale).matchups;
  const names = civNames(locale);
  if ("civ" in parsed) {
    const civ = names[parsed.civ];
    const values = { civ, civLower: civ.toLowerCase() };
    return { title: fill(t.civTitle, values), description: fill(t.civDescription, values), path: `/cruces/${parsed.civ}` };
  }
  if (!parsed.canonical) return null;
  const [a, b] = [names[parsed.a], names[parsed.b]];
  const values = { a, b, aLower: a.toLowerCase(), bLower: b.toLowerCase() };
  return { title: fill(t.pairTitle, values), description: fill(t.pairDescription, values), path: `/cruces/${pairSlug(parsed.a, parsed.b)}` };
}

/** Un rival con sus 30 casilleros de duración "todas": el índice es mapa · 6 + franja de Elo. */
export interface RivalRow {
  rival: string;
  cells: Cell[];
}

function grid(cellOf: (map: number, elo: number) => Cell | null): Cell[] | null {
  const cells: Cell[] = [];
  for (let map = 0; map < 5; map++) {
    for (let elo = 0; elo < 6; elo++) {
      const c = cellOf(map, elo);
      if (!c) return null;
      cells.push(c);
    }
  }
  return cells;
}

/** Los rivales de una civ en 1v1, del que mejor le va al que peor (en todos los mapas y todas las franjas). */
export function rivalsOf(data: MatchupData, civ: string): RivalRow[] {
  const key = civ.trim().toLowerCase();
  const rivals = new Set<string>();
  for (const pair of data.oneVsOne.keys()) {
    const [a, b] = pair.split("|");
    if (a === key) rivals.add(b);
    else if (b === key) rivals.add(a);
  }
  return [...rivals]
    .map((rival) => ({ rival, cells: grid((map, elo) => oneVsOne(data, key, rival, map, elo, 0)) }))
    .filter((row): row is RivalRow => row.cells !== null)
    .sort((x, y) => y.cells[0].p - x.cells[0].p);
}

/** Los 30 casilleros de la civ por equipos (mapa · 6 + franja de Elo), o null si no tiene datos. */
export function teamOf(data: MatchupData, civ: string): Cell[] | null {
  return grid((map, elo) => team(data, civ, map, elo, 0));
}

/** Los 720 enteros del cruce desde `a` (milésimas y partidas), para el gráfico por duración. */
export function pairCells(data: MatchupData, a: string, b: string): number[] {
  const out: number[] = [];
  for (let map = 0; map < 5; map++) {
    for (let elo = 0; elo < 6; elo++) {
      for (let duration = 0; duration < 6; duration++) {
        const c = oneVsOne(data, a, b, map, elo, duration);
        if (!c) throw new Error(`No hay datos del cruce ${a} contra ${b}.`);
        out.push(Math.round(c.p * 1000), Math.round(c.from * 1000), Math.round(c.to * 1000), c.n);
      }
    }
  }
  return out;
}

export interface PairSummary {
  general: Cell;
  /** La chance del rival en %, el complemento de la de `a` (redondeadas por separado, 50,5 y 49,5 daban 51 y 50). */
  rivalPercent: number;
  maps: { key: string | null; name: string; cell: Cell }[];
  /** El general no tiene partidas: todo sale del modelo. */
  estimated: boolean;
}

/** El cruce desde `a` en todos los mapas y todas las franjas de Elo. */
export function pairSummary(data: MatchupData, a: string, b: string, locale: Locale = "es"): PairSummary {
  const general = oneVsOne(data, a, b, 0, 0, 0);
  if (!general) throw new Error(`No hay datos del cruce ${a} contra ${b}.`);
  const maps = [
    { key: null, name: getDictionary(locale).matchups.allMaps, cell: general },
    ...data.mapKeys.map((key, i) => ({ key, name: mapName(key, locale), cell: oneVsOne(data, a, b, i + 1, 0, 0) ?? general })),
  ];
  return { general, rivalPercent: 100 - percent(general.p), maps, estimated: general.n === 0 };
}

/**
 * La frase de duración de lo elegido en el gráfico, con la regla de honestidad (spec 4.5): la tendencia, "sin
 * diferencia clara" si hay dos franjas con partidas cuyos rangos se tocan, el aviso de estimado si no hay ninguna, o
 * null si hay una sola. `labelOf` da la etiqueta de cada franja y `where`, lo elegido (selectionPlace).
 */
export function durationText(
  cellOf: (band: number) => Cell | null,
  civName: string,
  labelOf: (band: number) => string,
  where: string | null,
  locale: Locale = "es",
): string | null {
  const dict = getDictionary(locale).matchups;
  const t = trend(cellOf);
  if (t) {
    return fill(t.favorsShort ? dict.trendShort : dict.trendLong, {
      civ: civName,
      shortPercent: percent(t.short.p),
      shortLabel: labelOf(t.shortBand),
      longPercent: percent(t.long.p),
      longLabel: labelOf(t.longBand),
    });
  }
  const withGames = [1, 2, 3, 4, 5].filter((band) => (cellOf(band)?.n ?? 0) > 0).length;
  // "Sin diferencia clara", no "parejo": que los rangos se toquen no prueba que no haya diferencia.
  if (withGames >= 2) return dict.noClearDifference;
  return withGames === 0 ? mapLine({ p: 0, from: 0, to: 0, n: 0 }, where, locale) : null;
}

/** Lo elegido en el gráfico para el aviso de estimado: "Nómada", "esa franja de Elo", "Nómada en esa franja de Elo". */
export function selectionPlace(map: string | null, eloBand: number, locale: Locale = "es"): string | null {
  if (eloBand <= 0) return map;
  const t = getDictionary(locale).matchups;
  return map ? fill(t.selectionMapElo, { map }) : t.selectionElo;
}

/** El aviso de un casillero sin partidas (sale del modelo), o null si tiene. */
export function mapLine(cell: Cell, place: string | null, locale: Locale = "es"): string | null {
  if (cell.n > 0) return null;
  const t = getDictionary(locale).matchups;
  return place ? fill(t.estimatedIn, { place }) : t.estimated;
}
