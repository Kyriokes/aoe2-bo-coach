import { parseMatchups } from "./matchups";

/** Para los tests: [p, desde, hasta, n] de un casillero según (mapa 0..4, Elo 0..5, duración 0..5). */
export type CellFn = (map: number, elo: number, duration: number) => [number, number, number, number];

/** Para los tests: un matchup_stats.json con el formato que escribe tools/stats. */
export function buildMatchups(oneVsOne: Record<string, CellFn>, team: Record<string, CellFn>, extra: Record<string, unknown> = {}) {
  const values = (fn: CellFn) => {
    const out: number[] = [];
    for (let m = 0; m < 5; m++) for (let e = 0; e < 6; e++) for (let d = 0; d < 6; d++) out.push(...fn(m, e, d));
    return out;
  };
  return JSON.stringify({
    schema_version: 1,
    generated: "2026-10-25",
    source: { name: "aoe2-guide", url: "https://aoe2-guide.vercel.app/data", from: "2026-06-01", to: "2026-07-03" },
    patch: { label: "Update 185872", start: "2026-06-01", previous_mixed: false },
    matches: { "1v1": 100, team: 50 },
    bands: { elo: [850, 1050, 1300, 1600], game_minutes: [20, 30, 40, 55], maps: ["arabia", "arena", "black_forest", "nomad"] },
    interval: 0.9,
    one_vs_one: Object.fromEntries(Object.entries(oneVsOne).map(([k, fn]) => [k, values(fn)])),
    team: Object.fromEntries(Object.entries(team).map(([k, fn]) => [k, values(fn)])),
    ...extra,
  });
}

/** Todos los casilleros iguales. */
export const same = (p: number, from: number, to: number, n: number): CellFn => () => [p, from, to, n];

/** Lee el JSON de prueba (falla si no se puede). */
export function parseTest(json: string) {
  const data = parseMatchups(json);
  if (!data) throw new Error("no se leyó el JSON de prueba");
  return data;
}
