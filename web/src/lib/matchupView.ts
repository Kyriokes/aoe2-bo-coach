import { durationLabel, MAP_NAMES, oneVsOne, percent, team, trend, type Cell, type MatchupData } from "./matchups";

/**
 * Lo que necesita cada página de /cruces, sacado de las estadísticas: solo los casilleros de esa página, así el
 * navegador no recibe el archivo entero (3,7 MB) para cambiar el mapa o la franja de Elo.
 */

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
  /** La frase de duración con la regla de honestidad, o null si no hay dos franjas con partidas. */
  trendText: string | null;
  /** El general no tiene partidas: todo sale del modelo. */
  estimated: boolean;
}

/** El cruce desde `a` en todos los mapas y todas las franjas de Elo. `civName` es el nombre de `a` ("Britanos"). */
export function pairSummary(data: MatchupData, a: string, b: string, civName: string): PairSummary {
  const general = oneVsOne(data, a, b, 0, 0, 0);
  if (!general) throw new Error(`No hay datos del cruce ${a} contra ${b}.`);
  const maps = [
    { key: null, name: "Todos los mapas", cell: general },
    ...data.mapKeys.map((key, i) => ({ key, name: MAP_NAMES[key] ?? key, cell: oneVsOne(data, a, b, i + 1, 0, 0) ?? general })),
  ];
  const cellOf = (band: number) => oneVsOne(data, a, b, 0, 0, band);
  const t = trend(cellOf);
  let trendText: string | null = null;
  if (t) {
    trendText =
      `${civName} ganan más en partidas ${t.favorsShort ? "cortas" : "largas"}: ` +
      `${percent(t.short.p)} % en ${durationLabel(data, t.shortBand)}, ${percent(t.long.p)} % en ${durationLabel(data, t.longBand)}.`;
  } else if ([1, 2, 3, 4, 5].filter((band) => (cellOf(band)?.n ?? 0) > 0).length >= 2) {
    // "Sin diferencia clara", no "parejo": que los rangos se toquen no prueba que no haya diferencia (spec 4.5).
    trendText = "Sin diferencia clara por duración.";
  }
  return { general, maps, trendText, estimated: general.n === 0 };
}

/** El aviso de un casillero sin partidas (sale del modelo), o null si tiene. */
export function mapLine(cell: Cell, mapName: string | null): string | null {
  if (cell.n > 0) return null;
  return mapName ? `Estimado: no hay partidas de este cruce en ${mapName}.` : "Estimado: no hay partidas de este cruce.";
}
