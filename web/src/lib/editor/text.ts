import { fill, getDictionary } from "@/i18n";
import type { Locale } from "@/i18n/locales";
import { formatClock } from "../buildOrder";
import type { PlanBlock } from "../coach/types";
import { entityName, sourceName } from "../stepText";

/** Los textos del editor de BO que salen de funciones puras (D8). Los textos en sí están en el diccionario. */

/** Las edades, en castellano: de acá sale el tipo `Age` (ver constraints.ts). */
export const AGE_LABEL = getDictionary("es").age;

/** Una edad en ese idioma ("Feudal", "Castle"). */
export function ageLabel(age: keyof typeof AGE_LABEL, locale: Locale = "es") {
  return getDictionary(locale).age[age];
}

/** El motivo de un bloqueo del simulador, como lo muestra la página de cada BO. */
export function blockText(reason: PlanBlock["reason"], locale: Locale = "es") {
  return getDictionary(locale).block[reason];
}

/** "faltan 500 de comida" si se sabe cuánto, o el motivo general. */
export function blockReason(block: Pick<PlanBlock, "reason" | "missing">, missingAmount?: number | null, locale: Locale = "es") {
  const t = getDictionary(locale).block;
  if (block.reason === "resources" && block.missing) {
    const resource = t[block.missing];
    return missingAmount != null && missingAmount > 0 ? fill(t.missingAmount, { n: missingAmount, resource }) : fill(t.missing, { resource });
  }
  return t[block.reason];
}

/** "No antes de 8:10: faltan 500 de comida". */
export function notBeforeText(start: number, reason: string, locale: Locale = "es") {
  return fill(getDictionary(locale).editor.drag.notBefore, { time: formatClock(start), reason });
}

/** Lo que se dice de un paso que nunca arranca en ese lugar. */
export function neverStartsText(reason: string | null, locale: Locale = "es") {
  const t = getDictionary(locale).editor.drag;
  return reason ? fill(t.neverStartsReason, { reason }) : t.neverStarts;
}

/** El nombre de una caja de la paleta. */
export function paletteLabel(kind: "villagers" | "move" | "entity" | "age", key?: string, locale: Locale = "es") {
  const t = getDictionary(locale).editor.palette;
  switch (kind) {
    case "villagers":
      return fill(t.villagersTo, { source: sourceName(key, locale) });
    case "move":
      return t.moveVillagers;
    case "age":
      return fill(t.clickAge, { age: Object.hasOwn(AGE_LABEL, key ?? "") ? ageLabel(key as keyof typeof AGE_LABEL, locale) : (key ?? "") });
    default:
      return capitalize(entityName(key ?? "", 1, locale));
  }
}

export function capitalize(s: string) {
  return s.length === 0 ? s : s[0].toUpperCase() + s.slice(1);
}
