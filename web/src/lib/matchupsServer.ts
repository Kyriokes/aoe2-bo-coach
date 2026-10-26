import "server-only";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { parseMatchups, type MatchupData } from "./matchups";

let cached: MatchupData | null | undefined;

/**
 * Las estadísticas reales de cruces (public/coach/matchup_stats.json, que copia scripts/build-coach.mjs), leídas
 * una sola vez; null si faltan o no se entienden (las páginas lo dicen en vez de romper el build).
 */
export function getMatchups(): MatchupData | null {
  if (cached === undefined) {
    try {
      cached = parseMatchups(readFileSync(join(process.cwd(), "public", "coach", "matchup_stats.json"), "utf8"));
    } catch {
      cached = null;
    }
  }
  return cached;
}

/** Las civs que tienen estadísticas, en orden alfabético de su clave. */
export function civsWithData(data: MatchupData): string[] {
  const civs = new Set<string>(data.team.keys());
  for (const pair of data.oneVsOne.keys()) for (const civ of pair.split("|")) civs.add(civ);
  return [...civs].sort();
}
