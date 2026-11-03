import { fill, getDictionary } from "@/i18n";
import type { Locale } from "@/i18n/locales";
import { formatClock } from "./buildOrder";
import { civNames } from "./civNames";

export const SITE_NAME = "AoE2 BO Coach";

/** "Cualquier civ" para "any"; si no, el nombre en ese idioma ("Britanos"), o la clave si no es una civ conocida. */
export function civLabel(civ: string, locale: Locale = "es") {
  if (civ === "any") return getDictionary(locale).civ.any;
  const names = civNames(locale);
  return Object.hasOwn(names, civ.toLowerCase()) ? names[civ.toLowerCase()] : civ;
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
export function byline(bo: Pick<ShareInfo, "author" | "uploader">, locale: Locale = "es") {
  const t = getDictionary(locale);
  // Sin autor ni cuenta (un perfil borrado): la base no guarda un nombre y se dice en el idioma de la página.
  const author = bo.author || t.user.anonymous;
  const same = !bo.uploader || bo.author.trim().toLowerCase() === bo.uploader.trim().toLowerCase();
  return same ? author : fill(t.bo.uploadedBy, { author, uploader: bo.uploader ?? "" });
}

/** "Cualquier civ · Feudal 8:10 · Castillos 16:00 · por Ana": lo que se ve de una BO sin abrirla. */
export function shareFacts(bo: ShareInfo, locale: Locale = "es") {
  const t = getDictionary(locale);
  return [
    civLabel(bo.civ, locale),
    bo.feudal_click !== null ? fill(t.bo.feudal, { time: formatClock(bo.feudal_click) }) : null,
    bo.castle_click !== null ? fill(t.bo.castle, { time: formatClock(bo.castle_click) }) : null,
    fill(t.bo.by, { author: byline(bo, locale) }),
  ]
    .filter((part) => part !== null)
    .join(" · ");
}

/**
 * Descripción de la vista previa de un enlace (Discord, WhatsApp, buscadores): los datos de la BO y el principio de
 * su descripción, en un solo renglón y sin pasarse de <paramref name="maxLength"/> caracteres.
 */
export function shareDescription(bo: ShareInfo, maxLength = 200, locale: Locale = "es") {
  const facts = shareFacts(bo, locale);
  const description = bo.description?.replace(/\s+/g, " ").trim();
  const text = description ? `${facts}. ${description}` : facts;
  return text.length <= maxLength ? text : `${text.slice(0, maxLength - 1).trimEnd()}…`;
}
