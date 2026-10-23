import { formatClock } from "../buildOrder";
import type { PlanBlock } from "../coach/types";
import { entityName, sourceName } from "../stepText";

/** Los textos del editor de BO (D8), en castellano rioplatense y sin guiones largos. */

export const AGE_LABEL = { dark: "Edad Oscura", feudal: "Feudal", castle: "Castillos", imperial: "Imperial" } as const;

/** El motivo de un bloqueo del simulador, como lo muestra la página de cada BO. */
export const BLOCK_TEXT: Record<PlanBlock["reason"], string> = {
  population: "falta población (casas)",
  resources: "faltan recursos",
  producer: "falta el edificio que lo produce",
  villagers: "no hay aldeanos libres",
  age: "todavía no se llegó a la edad",
};

const RESOURCE_LABEL: Record<NonNullable<PlanBlock["missing"]>, string> = {
  food: "comida",
  wood: "madera",
  gold: "oro",
  stone: "piedra",
};

/** "faltan 500 de comida" si se sabe cuánto, o el motivo general. */
export function blockReason(block: Pick<PlanBlock, "reason" | "missing">, missingAmount?: number | null) {
  if (block.reason === "resources" && block.missing) {
    return missingAmount != null && missingAmount > 0 ? `faltan ${missingAmount} de ${RESOURCE_LABEL[block.missing]}` : `falta ${RESOURCE_LABEL[block.missing]}`;
  }
  return BLOCK_TEXT[block.reason];
}

/** "No antes de 8:10: faltan 500 de comida". */
export function notBeforeText(start: number, reason: string) {
  return `No antes de ${formatClock(start)}: ${reason}`;
}

/** Lo que se dice de un paso que nunca arranca en ese lugar. */
export function neverStartsText(reason: string | null) {
  return reason ? `Acá no arranca: ${reason}` : "Acá no arranca: el paso anterior tampoco termina";
}

export const DROP_OUTSIDE_TEXT = "Soltá sobre la línea de tiempo";
/** Deslizando una caja en vertical para cambiar la cantidad. */
export const COUNT_HINT = "(↑ más · ↓ menos)";
export const PINNED_TEXT = "Este paso no se puede mover";

/** El nombre de una caja de la paleta. */
export function paletteLabel(kind: "villagers" | "move" | "entity" | "age", key?: string) {
  switch (kind) {
    case "villagers":
      return `Aldeanos a ${sourceName(key)}`;
    case "move":
      return "Mover aldeanos";
    case "age":
      return `Clic en ${AGE_LABEL[key as keyof typeof AGE_LABEL] ?? key}`;
    default:
      return capitalize(entityName(key ?? "", 1));
  }
}

export function capitalize(s: string) {
  return s.length === 0 ? s : s[0].toUpperCase() + s.slice(1);
}
