import { dbErrorMessage, dbErrorStatus } from "@/lib/dbErrors";
import { inBatches, isAppVersion, isGameId, MAX_RECORD_BYTES, recordIdentity, recordProblem, STORAGE_REMOVE_MAX } from "@/lib/gameRecord";
import { publicClient } from "@/lib/supabase/server";
import { error } from "../../bo/appApi";

const BUCKET = "registros";

/**
 * POST /api/app/registros: el registro de una partida, en gzip, con la identidad de la instalación en las cabeceras
 * (X-BoCoach-Install, -Secret, -Game y -Version; sin cuenta). 201 { duplicate: false }, o 200 { duplicate: true } si ya
 * estaba: reenviar no duplica. La app reintenta más tarde ante un 429 o un 5xx; descarta el registro ante un 400, 413 o 422,
 * y ante un 401 o 403 corta la tanda y lo descarta recién a las 5 negativas (el código puede venir de un proxy, no de acá).
 */
export async function POST(request: Request) {
  const identity = recordIdentity(request.headers);
  if (!identity) return error(401, "Falta la identidad de la instalación.");
  const game = request.headers.get("x-bocoach-game");
  if (!isGameId(game)) return error(400, "Falta el identificador de la partida.");
  const version = request.headers.get("x-bocoach-version");
  if (!isAppVersion(version)) return error(400, "Falta la versión de la app.");
  // Antes de leerlo, si dice lo que pesa.
  if (Number(request.headers.get("content-length") ?? 0) > MAX_RECORD_BYTES) return error(413, "El registro tiene que pesar hasta 1 MB.");

  const bytes = new Uint8Array(await request.arrayBuffer());
  if (bytes.length > MAX_RECORD_BYTES) return error(413, "El registro tiene que pesar hasta 1 MB.");
  const problem = recordProblem(bytes, game);
  if (problem) return error(400, problem);

  const db = publicClient();
  if (!db) return error(503, "La web todavía no está conectada a la base de datos.");
  const reserved = await db.rpc("app_record_reserve", {
    p_install: identity.install,
    p_secret: identity.secret,
    p_game: game,
    p_size: bytes.length,
    p_version: version,
  });
  if (reserved.error) return error(dbErrorStatus(reserved.error), dbErrorMessage(reserved.error));
  const { path, status } = reserved.data as { path: string; status: "new" | "exists" };
  if (status === "exists") return Response.json({ duplicate: true });

  const uploaded = await db.storage.from(BUCKET).upload(path, bytes, { contentType: "application/gzip", upsert: false });
  // Ya estaba: se subió y falló lo que seguía. No hay nada que subir de nuevo.
  const alreadyThere = uploaded.error && "statusCode" in uploaded.error && uploaded.error.statusCode === "409";
  if (uploaded.error && !alreadyThere) return error(502, "No se pudo guardar el registro. Probá de nuevo en un rato.");

  const done = await db.rpc("app_record_done", { p_install: identity.install, p_secret: identity.secret, p_game: game });
  if (done.error) return error(dbErrorStatus(done.error), dbErrorMessage(done.error));
  return Response.json({ duplicate: false }, { status: 201 });
}

/** DELETE /api/app/registros: el pedido de borrado de lo que subió esta instalación. { forgotten: cuántos }. */
export async function DELETE(request: Request) {
  const identity = recordIdentity(request.headers);
  if (!identity) return error(401, "Falta la identidad de la instalación.");
  const db = publicClient();
  if (!db) return error(503, "La web todavía no está conectada a la base de datos.");
  const forgotten = await db.rpc("app_record_forget", { p_install: identity.install, p_secret: identity.secret });
  if (forgotten.error) return error(dbErrorStatus(forgotten.error), dbErrorMessage(forgotten.error));
  const paths = (forgotten.data ?? []) as string[];
  for (const batch of inBatches(paths, STORAGE_REMOVE_MAX)) {
    const removed = await db.storage.from(BUCKET).remove(batch);
    if (removed.error) return error(502, "No se pudieron borrar los registros. Probá de nuevo en un rato.");
  }
  return Response.json({ forgotten: paths.length });
}
