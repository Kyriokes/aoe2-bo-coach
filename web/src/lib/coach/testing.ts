import { readFileSync } from "node:fs";
import { join } from "node:path";
import { pathToFileURL } from "node:url";
import { startCoach, type CoachRuntime } from "./runtime";

/**
 * El Core en WebAssembly corrido en Node para los tests: el mismo runtime que carga el navegador desde public/coach
 * (hay que correr antes `npm run coach`; `pretest` lo hace).
 */
export const coachDir = join(__dirname, "..", "..", "..", "public", "coach");
export const repoDir = join(__dirname, "..", "..", "..", "..");

export async function startTestCoach(): Promise<CoachRuntime> {
  const dotnetModule = await import(pathToFileURL(join(coachDir, "_framework", "dotnet.js")).href);
  return startCoach(
    dotnetModule,
    readFileSync(join(coachDir, "game_data.json"), "utf8"),
    readFileSync(join(coachDir, "events.json"), "utf8"),
    readFileSync(join(coachDir, "maps.json"), "utf8"),
  );
}
