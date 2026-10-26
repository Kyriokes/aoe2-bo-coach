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
