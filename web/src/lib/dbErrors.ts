/** Lo que hace falta de un error de Supabase (PostgrestError) para explicarlo. */
export interface DbError {
  code?: string;
  message: string;
}

/** Los topes que controla la base (migración 20261022000000_topes.sql), contados para quien publica. */
export const LIMITS_MESSAGE =
  "La BO no entra en los topes de la biblioteca: hasta 512 KB, de 1 a 300 pasos, count y builders de 1 a 200 y etiquetas de hasta 30 caracteres.";

/**
 * El mensaje para mostrar de un error de la base. Los que arma la migración ya están en castellano y se muestran
 * tal cual; una restricción violada se explica con los topes, y lo inesperado no muestra los detalles internos
 * (nombres de tablas, consultas).
 */
export function dbErrorMessage(error: DbError): string {
  if (/violates check constraint/i.test(error.message)) return LIMITS_MESSAGE;
  if (/row-level security|permission denied/i.test(error.message)) return "No tenés permiso para hacer eso.";
  // 28000 token inválido, 42501 sin permiso, 23514 un tope de la migración, P0002 una BO que no existe o no es
  // pública (con su mensaje en castellano).
  if (["28000", "42501", "23514", "P0002"].includes(error.code ?? "")) return error.message;
  return "Error de la base de datos. Probá de nuevo en un rato.";
}

/** El código HTTP de un error de la base para la API de la app. */
export function dbErrorStatus(error: DbError): number {
  if (error.code === "28000") return 401;
  if (error.code === "42501") return 403;
  if (error.code === "P0002") return 404;
  if (error.code === "23514" || /violates check constraint/i.test(error.message)) return 400;
  return 500;
}
