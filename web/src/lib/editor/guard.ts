import { fill, getDictionary } from "@/i18n";
import type { Locale } from "@/i18n/locales";
import type { Analysis } from "../coach/types";
import { AGE_UP_REQ, type GameDataLite } from "./constraints";
import { lastWait, waitReason } from "./explain";
import type { EditorAction, EditorStep } from "./model";
import { entityName } from "../stepText";
import { ageLabel } from "./text";

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
export function brokenBy(before: Analysis | null, after: Analysis | null, steps: EditorStep[], data: GameDataLite | null, locale: Locale = "es"): string | null {
  if (!before || !after) return null;
  const known = new Set(before.issues.filter((i) => i.severity === "error").map((i) => i.message));
  const fresh = after.issues.find((i) => i.severity === "error" && !known.has(i.message));
  if (fresh) return fresh.message;
  if (before.valid && after.valid && after.completed === false) {
    // Un paso que no arrancaba antes no frena; uno nuevo que no arranca sí, aunque la BO ya tuviera otro trabado (así se
    // agregaba el clic en Castillos sin sus 2 edificios).
    const stuckBefore = new Set(before.steps.filter((s) => s.start === null).map((s) => s.id));
    const stuck = after.steps.find((s) => s.start === null && !stuckBefore.has(s.id));
    const step = steps.find((s) => s.id === stuck?.id);
    if (stuck && step) return fill(getDictionary(locale).editor.guard.neverStarts, { step: stuck.text, reason: neverStartsReason(after, [step], data, locale) });
  }
  return null;
}

/** Por qué un paso nunca arranca, dicho para que se pueda arreglar ("falta un establo en la BO: agregalo antes"). */
export function neverStartsReason(analysis: Analysis, block: EditorStep[], data: GameDataLite | null, locale: Locale = "es"): string {
  const t = getDictionary(locale).editor.guard;
  const wait = lastWait(analysis, block);
  const step = block.find((s) => s.id === wait?.step_id) ?? block[0];
  const ageUp = step?.do === "research" && step.what ? AGE_UP_REQ[step.what] : undefined;
  if (wait?.reason === "producer" && ageUp) {
    return fill(t.missingTwo, { age: ageLabel(ageUp.requires, locale), buildings: ageUp.buildings.filter((b) => b !== "dock").map((b) => entityName(b, 1, locale)).join(", ") });
  }
  if (wait?.reason === "producer" && step?.what) {
    const producer = data?.entities[step.what]?.made_in ?? data?.entities[step.what]?.requires;
    if (producer) return fill(t.missingBuilding, { building: withArticle(producer, locale) });
  }
  return waitReason(analysis, block, data, locale) ?? t.previousNotEnding;
}

const FEMININE = new Set(["archery_range", "blacksmith", "university", "farm", "house", "palisade_wall", "stone_wall", "watch_tower"]);

/** "un establo", "una galería de tiro", "an archery range". */
export function withArticle(key: string, locale: Locale = "es") {
  const t = getDictionary(locale).editor.guard;
  const name = entityName(key, 1, locale);
  if (locale === "en") return fill(/^[aeiou]/.test(name) && !name.startsWith("uni") ? t.articleVowel : t.articleMasc, { name });
  return fill(FEMININE.has(key) ? t.articleFem : t.articleMasc, { name });
}
