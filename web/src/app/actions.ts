"use server";

import { updateTag } from "next/cache";
import { redirect } from "next/navigation";
import { columnsFor, parseBuildOrder, type Visibility } from "@/lib/buildOrder";
import { buildOrderTag, LIBRARY_TAG } from "@/lib/data";
import { dbErrorMessage } from "@/lib/dbErrors";
import { sessionClient } from "@/lib/supabase/server";

export type ActionResult<T = null> = { ok: true; value: T } | { ok: false; error: string };

const notLoggedIn = { ok: false as const, error: "Iniciá sesión para hacer esto." };

async function userClient() {
  const supabase = await sessionClient();
  const { data } = await supabase.auth.getClaims();
  return data?.claims?.sub ? supabase : null;
}

/** Publica una BO nueva con la cuenta del usuario. Devuelve el código corto. */
export async function publishBuildOrder(
  json: string,
  feudalClick: number | null,
  castleClick: number | null,
  visibility: Visibility,
): Promise<ActionResult<string>> {
  const parsed = parseBuildOrder(json);
  if (!parsed.ok) return parsed;
  const supabase = await userClient();
  if (!supabase) return notLoggedIn;

  const { data, error } = await supabase
    .from("build_orders")
    .insert(columnsFor(parsed.body, feudalClick, castleClick, visibilityOf(visibility)))
    .select("code")
    .single();
  if (error) return { ok: false, error: `No se pudo publicar: ${dbErrorMessage(error)}` };
  updateTag(LIBRARY_TAG);
  return { ok: true, value: (data as { code: string }).code };
}

/** Reemplaza una BO propia (las políticas de la base no dejan tocar las ajenas). */
export async function updateBuildOrder(
  code: string,
  json: string,
  feudalClick: number | null,
  castleClick: number | null,
  visibility: Visibility,
): Promise<ActionResult> {
  const parsed = parseBuildOrder(json);
  if (!parsed.ok) return parsed;
  const supabase = await userClient();
  if (!supabase) return notLoggedIn;

  const { data, error } = await supabase
    .from("build_orders")
    .update(columnsFor(parsed.body, feudalClick, castleClick, visibilityOf(visibility)))
    .eq("code", code)
    .select("code");
  if (error) return { ok: false, error: `No se pudo guardar: ${dbErrorMessage(error)}` };
  if (!data?.length) return { ok: false, error: "Esa BO no es tuya." };
  updateTag(LIBRARY_TAG);
  updateTag(buildOrderTag(code));
  return { ok: true, value: null };
}

/** Pública o privada. Invalida la biblioteca y la página: una que pasa a privada deja de verse para los demás. */
export async function setVisibility(code: string, visibility: Visibility): Promise<ActionResult> {
  const supabase = await userClient();
  if (!supabase) return notLoggedIn;
  const { data, error } = await supabase
    .from("build_orders")
    .update({ visibility: visibilityOf(visibility) })
    .eq("code", code)
    .select("code");
  if (error) return { ok: false, error: `No se pudo cambiar: ${dbErrorMessage(error)}` };
  if (!data?.length) return { ok: false, error: "Esa BO no es tuya." };
  updateTag(LIBRARY_TAG);
  updateTag(buildOrderTag(code));
  return { ok: true, value: null };
}

/** Suma o saca una BO pública de otro de las que usa el coach del usuario. */
export async function setSubscription(buildOrderId: string, subscribe: boolean): Promise<ActionResult> {
  const supabase = await userClient();
  if (!supabase) return notLoggedIn;
  const { error } = subscribe
    ? await supabase.from("subscriptions").upsert({ build_order_id: buildOrderId }, { ignoreDuplicates: true })
    : await supabase.from("subscriptions").delete().eq("build_order_id", buildOrderId);
  if (error) return { ok: false, error: `No se pudo ${subscribe ? "sumar" : "sacar"}: ${dbErrorMessage(error)}` };
  return { ok: true, value: null };
}

export async function deleteBuildOrder(code: string): Promise<ActionResult> {
  const supabase = await userClient();
  if (!supabase) return notLoggedIn;
  const { data, error } = await supabase.from("build_orders").delete().eq("code", code).select("code");
  if (error) return { ok: false, error: `No se pudo borrar: ${dbErrorMessage(error)}` };
  if (!data?.length) return { ok: false, error: "Esa BO no es tuya." };
  updateTag(LIBRARY_TAG);
  updateTag(buildOrderTag(code));
  return { ok: true, value: null };
}

/** Pone o saca el voto del usuario. */
export async function setVote(buildOrderId: string, code: string, vote: boolean): Promise<ActionResult> {
  const supabase = await userClient();
  if (!supabase) return notLoggedIn;
  const { error } = vote
    ? await supabase.from("votes").upsert({ build_order_id: buildOrderId }, { ignoreDuplicates: true })
    : await supabase.from("votes").delete().eq("build_order_id", buildOrderId);
  if (error) return { ok: false, error: `No se pudo votar: ${dbErrorMessage(error)}` };
  updateTag(LIBRARY_TAG);
  updateTag(buildOrderTag(code));
  return { ok: true, value: null };
}

/** Crea un token para la app de escritorio. Se muestra una sola vez: en la base queda solo su hash. */
export async function createAppToken(label: string): Promise<ActionResult<string>> {
  const supabase = await userClient();
  if (!supabase) return notLoggedIn;
  const { data, error } = await supabase.rpc("create_app_token", { p_label: label.trim().slice(0, 60) || "BO Coach" });
  if (error) return { ok: false, error: `No se pudo crear el token: ${dbErrorMessage(error)}` };
  return { ok: true, value: data as string };
}

export async function revokeAppToken(id: string): Promise<ActionResult> {
  const supabase = await userClient();
  if (!supabase) return notLoggedIn;
  const { error } = await supabase.from("app_tokens").delete().eq("id", id);
  if (error) return { ok: false, error: `No se pudo borrar el token: ${dbErrorMessage(error)}` };
  return { ok: true, value: null };
}

/** Lo que llega del navegador: cualquier cosa que no sea "public" se guarda privada. */
function visibilityOf(value: unknown): Visibility {
  return value === "public" ? "public" : "private";
}

export async function signOut() {
  const supabase = await sessionClient();
  await supabase.auth.signOut();
  redirect("/");
}
