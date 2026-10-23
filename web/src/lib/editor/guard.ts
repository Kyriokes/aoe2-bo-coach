import type { Analysis } from "../coach/types";
import type { GameDataLite } from "./constraints";
import { lastWait, waitReason } from "./explain";
import type { EditorAction, EditorStep } from "./model";
import { entityName } from "../stepText";

/**
 * "No lo deja y explica" (D8): ninguna edición deja la BO rota. Se compara el análisis de antes con el de después:
 * si aparece un error nuevo o un paso deja de arrancar, la edición se rechaza con el motivo.
 */

/** Las acciones que no se controlan: cargar, empezar de nuevo, elegir, lo ya controlado y los textos libres. */
export function isGuarded(action: EditorAction): boolean {
  switch (action.type) {
    case "load":
    case "new":
    case "select":
    case "commit":
    case "analyzed":
    case "loadFailed":
    case "setTags":
      return false;
    case "setField":
      return action.field === "civ";
    default:
      return true;
  }
}

/** El motivo para rechazar el cambio de `before` a `after`, o null si se puede. */
export function brokenBy(before: Analysis | null, after: Analysis | null, steps: EditorStep[], data: GameDataLite | null): string | null {
  if (!before || !after) return null;
  const known = new Set(before.issues.filter((i) => i.severity === "error").map((i) => i.message));
  const fresh = after.issues.find((i) => i.severity === "error" && !known.has(i.message));
  if (fresh) return fresh.message;
  if (before.valid && before.completed !== false && after.valid && after.completed === false) {
    const stuck = after.steps.find((s) => s.start === null);
    const step = steps.find((s) => s.id === stuck?.id);
    if (stuck && step) return `Así "${stuck.text}" no arranca nunca: ${neverStartsReason(after, [step], data)}`;
  }
  return null;
}

/** Por qué un paso nunca arranca, dicho para que se pueda arreglar ("falta un establo en la BO: agregalo antes"). */
export function neverStartsReason(analysis: Analysis, block: EditorStep[], data: GameDataLite | null): string {
  const wait = lastWait(analysis, block);
  const step = block.find((s) => s.id === wait?.step_id) ?? block[0];
  if (wait?.reason === "producer" && step?.what) {
    const producer = data?.entities[step.what]?.made_in;
    if (producer) return `falta ${withArticle(producer)} en la BO: agregalo antes`;
  }
  return waitReason(analysis, block, data) ?? "el paso anterior tampoco termina";
}

const FEMININE = new Set(["archery_range", "blacksmith", "university", "farm", "house", "palisade_wall", "stone_wall", "watch_tower"]);

/** "un establo", "una galería de tiro". */
export function withArticle(key: string) {
  return `${FEMININE.has(key) ? "una" : "un"} ${entityName(key, 1)}`;
}
