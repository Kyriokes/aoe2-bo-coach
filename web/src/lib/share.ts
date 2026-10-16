import { formatClock } from "./buildOrder";

export const SITE_NAME = "AoE2 BO Coach";

/** "Cualquier civ" para "any"; si no, la clave de la civ ("britons"). */
export function civLabel(civ: string) {
  return civ === "any" ? "Cualquier civ" : civ;
}

/** Lo que hace falta de una BO para mostrarla al compartir el enlace. */
export interface ShareInfo {
  name: string;
  civ: string;
  author: string;
  description: string | null;
  feudal_click: number | null;
  castle_click: number | null;
}

/** "Cualquier civ · Feudal 8:10 · Castillos 16:00 · por Ana": lo que se ve de una BO sin abrirla. */
export function shareFacts(bo: ShareInfo) {
  return [
    civLabel(bo.civ),
    bo.feudal_click !== null ? `Feudal ${formatClock(bo.feudal_click)}` : null,
    bo.castle_click !== null ? `Castillos ${formatClock(bo.castle_click)}` : null,
    `por ${bo.author}`,
  ]
    .filter((part) => part !== null)
    .join(" · ");
}

/**
 * Descripción de la vista previa de un enlace (Discord, WhatsApp, buscadores): los datos de la BO y el principio de
 * su descripción, en un solo renglón y sin pasarse de <paramref name="maxLength"/> caracteres.
 */
export function shareDescription(bo: ShareInfo, maxLength = 200) {
  const facts = shareFacts(bo);
  const description = bo.description?.replace(/\s+/g, " ").trim();
  const text = description ? `${facts}. ${description}` : facts;
  return text.length <= maxLength ? text : `${text.slice(0, maxLength - 1).trimEnd()}…`;
}
