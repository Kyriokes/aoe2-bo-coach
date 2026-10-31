import "server-only";
import { revalidateTag } from "next/cache";
import { parseBuildOrder } from "@/lib/buildOrder";
import { buildOrderTag, LIBRARY_TAG } from "@/lib/data";
import { dbErrorMessage, dbErrorStatus } from "@/lib/dbErrors";
import { publicClient } from "@/lib/supabase/server";

/**
 * Lo que comparten los endpoints que usa la app de escritorio para publicar. La app manda su token personal
 * (Authorization: Bearer bocoach_...) y la base lo valida en app_publish: la web no necesita la clave de
 * administrador de Supabase.
 */
export function error(status: number, message: string) {
  return Response.json({ error: message }, { status });
}

export function bearerToken(request: Request) {
  const header = request.headers.get("authorization") ?? "";
  const match = /^Bearer\s+(bocoach_[0-9a-f]{64})$/i.exec(header.trim());
  return match?.[1] ?? null;
}

/** Publica (sin código) o reemplaza (con el código de una BO propia). */
export async function publishFromApp(request: Request, code: string | null) {
  const token = bearerToken(request);
  if (!token) return error(401, "Falta el token de la app (creá uno en Mi cuenta de la web).");

  let payload: { build_order?: unknown; feudal_click?: unknown; castle_click?: unknown };
  try {
    payload = await request.json();
  } catch {
    return error(400, "El cuerpo no es un JSON válido.");
  }
  const parsed = parseBuildOrder(payload.build_order);
  if (!parsed.ok) return error(400, parsed.error);

  const db = publicClient();
  if (!db) return error(503, "La web todavía no está conectada a la base de datos.");
  const seconds = (value: unknown) => (typeof value === "number" && Number.isFinite(value) && value >= 0 ? Math.round(value) : null);
  const { data, error: dbError } = await db.rpc("app_publish", {
    p_token: token,
    p_body: parsed.body,
    p_feudal_click: seconds(payload.feudal_click),
    p_castle_click: seconds(payload.castle_click),
    p_code: code,
  });
  if (dbError) return error(dbErrorStatus(dbError), dbErrorMessage(dbError));

  const published = data as string;
  revalidateTag(LIBRARY_TAG, "max");
  revalidateTag(buildOrderTag(published), "max");
  return Response.json({ code: published, url: `${new URL(request.url).origin}/bo/${published}` }, { status: code ? 200 : 201 });
}

/**
 * Llama a una función app_* de la base con el token de la app. Sin token 401, sin base 503, y los errores de la base
 * con su código (dbErrorStatus). Devuelve los datos o la respuesta de error.
 */
export async function appRpc<T>(request: Request, fn: string, args: Record<string, unknown>): Promise<{ data: T } | { response: Response }> {
  const token = bearerToken(request);
  if (!token) return { response: error(401, "Falta el token de la app (creá uno en Mi cuenta de la web).") };
  const db = publicClient();
  if (!db) return { response: error(503, "La web todavía no está conectada a la base de datos.") };
  const { data, error: dbError } = await db.rpc(fn, { p_token: token, ...args });
  if (dbError) return { response: error(dbErrorStatus(dbError), dbErrorMessage(dbError)) };
  return { data: data as T };
}
