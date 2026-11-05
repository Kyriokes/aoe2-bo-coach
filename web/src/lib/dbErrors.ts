import { getDictionary } from "@/i18n";
import type { Locale } from "@/i18n/locales";

/** Lo que hace falta de un error de Supabase (PostgrestError) para explicarlo. */
export interface DbError {
  code?: string;
  message: string;
}

/** Los topes que controla la base (migración 20261022000000_topes.sql), contados para quien publica. */
/** El tope del autor que dice el JSON (migración 20261031200000_columnas_del_json.sql). */
export const AUTHOR_MESSAGE = getDictionary("es").dbErrors.author;

export const LIMITS_MESSAGE = getDictionary("es").dbErrors.limits;

/**
 * El mensaje para mostrar de un error de la base. Los que arma la migración ya están en castellano y se muestran
 * tal cual; una restricción violada se explica con los topes, y lo inesperado no muestra los detalles internos
 * (nombres de tablas, consultas).
 */
export function dbErrorMessage(error: DbError, locale: Locale = "es"): string {
  const t = getDictionary(locale).dbErrors;
  if (/build_orders_author_ok/.test(error.message)) return t.author;
  if (/violates check constraint/i.test(error.message)) return t.limits;
  if (/row-level security|permission denied/i.test(error.message)) return t.forbidden;
  // 28000 token inválido, 42501 sin permiso, 23514 un tope de la migración, P0002 una BO que no existe o no es
  // pública, BC429 un límite por día de los registros de partida, BC430 una BO cambiada hace unos segundos. Sus mensajes
  // vienen en castellano de la migración; en otro idioma se explican por el código.
  if (["28000", "42501", "23514", "P0002", "BC429", "BC430"].includes(error.code ?? "")) {
    if (locale === "es") return error.message;
    if (error.code === "28000") return t.badToken;
    if (error.code === "42501") return t.forbidden;
    if (error.code === "P0002") return t.notPublic;
    if (error.code === "BC429") return t.dailyLimit;
    if (error.code === "BC430") return t.tooFast;
    return t.limits;
  }
  return t.generic;
}

/** El código HTTP de un error de la base para la API de la app. */
export function dbErrorStatus(error: DbError): number {
  if (error.code === "28000") return 401;
  if (error.code === "42501") return 403;
  if (error.code === "P0002") return 404;
  if (error.code === "BC429" || error.code === "BC430") return 429;
  if (error.code === "23514" || /violates check constraint/i.test(error.message)) return 400;
  return 500;
}
