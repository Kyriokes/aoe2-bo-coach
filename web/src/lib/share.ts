import { formatClock } from "./buildOrder";
import { CIV_NAMES } from "./civNames";

export const SITE_NAME = "AoE2 BO Coach";

/** "Cualquier civ" para "any"; si no, el nombre en castellano ("Britanos"), o la clave si no es una civ conocida. */
export function civLabel(civ: string) {
  if (civ === "any") return "Cualquier civ";
  return Object.hasOwn(CIV_NAMES, civ.toLowerCase()) ? CIV_NAMES[civ.toLowerCase()] : civ;
}

/** Lo que hace falta de una BO para mostrarla al compartir el enlace. */
export interface ShareInfo {
  name: string;
  civ: string;
  /** Quien armó la guía (el autor del JSON, o la cuenta que la subió si no dice). */
  author: string;
  /** La cuenta que la subió a la web. */
  uploader?: string;
  description: string | null;
  feudal_click: number | null;
  castle_click: number | null;
}

/**
 * "Hera, subida por Ana" si la guía es de otro (una importada de buildorderguide.com), o solo "Ana". La cuenta
 * siempre se ve: el autor del JSON lo escribe cualquiera.
 */
export function byline(bo: Pick<ShareInfo, "author" | "uploader">) {
  const same = !bo.uploader || bo.author.trim().toLowerCase() === bo.uploader.trim().toLowerCase();
  return same ? bo.author : `${bo.author}, subida por ${bo.uploader}`;
}

/** "Cualquier civ · Feudal 8:10 · Castillos 16:00 · por Ana": lo que se ve de una BO sin abrirla. */
export function shareFacts(bo: ShareInfo) {
  return [
    civLabel(bo.civ),
    bo.feudal_click !== null ? `Feudal ${formatClock(bo.feudal_click)}` : null,
    bo.castle_click !== null ? `Castillos ${formatClock(bo.castle_click)}` : null,
    `por ${byline(bo)}`,
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
