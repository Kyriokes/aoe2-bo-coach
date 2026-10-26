import { MAP_NAMES, oneVsOne, percent, team, trend, type Cell, type MatchupData } from "./matchups";

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
  maps: { key: string | null; name: string; cell: Cell }[];
  /** El general no tiene partidas: todo sale del modelo. */
  estimated: boolean;
}

/** El cruce desde `a` en todos los mapas y todas las franjas de Elo. */
export function pairSummary(data: MatchupData, a: string, b: string): PairSummary {
  const general = oneVsOne(data, a, b, 0, 0, 0);
  if (!general) throw new Error(`No hay datos del cruce ${a} contra ${b}.`);
  const maps = [
    { key: null, name: "Todos los mapas", cell: general },
    ...data.mapKeys.map((key, i) => ({ key, name: MAP_NAMES[key] ?? key, cell: oneVsOne(data, a, b, i + 1, 0, 0) ?? general })),
  ];
  return { general, maps, estimated: general.n === 0 };
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
): string | null {
  const t = trend(cellOf);
  if (t) {
    return (
      `${civName} ganan más en partidas ${t.favorsShort ? "cortas" : "largas"}: ` +
      `${percent(t.short.p)} % en ${labelOf(t.shortBand)}, ${percent(t.long.p)} % en ${labelOf(t.longBand)}.`
    );
  }
  const withGames = [1, 2, 3, 4, 5].filter((band) => (cellOf(band)?.n ?? 0) > 0).length;
  // "Sin diferencia clara", no "parejo": que los rangos se toquen no prueba que no haya diferencia.
  if (withGames >= 2) return "Sin diferencia clara por duración.";
  return withGames === 0 ? mapLine({ p: 0, from: 0, to: 0, n: 0 }, where) : null;
}

/** Lo elegido en el gráfico para el aviso de estimado: "Nómada", "esa franja de Elo", "Nómada en esa franja de Elo". */
export function selectionPlace(mapName: string | null, eloBand: number): string | null {
  if (eloBand <= 0) return mapName;
  return mapName ? `${mapName} en esa franja de Elo` : "esa franja de Elo";
}

/** El aviso de un casillero sin partidas (sale del modelo), o null si tiene. */
export function mapLine(cell: Cell, mapName: string | null): string | null {
  if (cell.n > 0) return null;
  return mapName ? `Estimado: no hay partidas de este cruce en ${mapName}.` : "Estimado: no hay partidas de este cruce.";
}
