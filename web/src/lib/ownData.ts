import "server-only";
import type { Visibility } from "./buildOrder";
import { CODE_PATTERN, summaryColumns, toSummary, type BuildOrderDetail, type BuildOrderSummary, type Row } from "./data";
import { supabaseConfigured } from "./supabase/env";
import { sessionClient } from "./supabase/server";

/**
 * Lo que depende de quién está logueado: sus BO (también las privadas) y sus suscripciones. Lee las cookies y no
 * pasa por el caché, así que se usa dentro de <Suspense>. Los permisos los pone la base: con la sesión de otro no se
 * ve nada ajeno que sea privado. Un error de la base no rompe la página: se anota y se sigue como si no hubiera nada
 * (lo propio solo alimenta botones y la cuenta; antes de correr la migración la base no tiene suscripciones).
 */
async function userClient() {
  if (!supabaseConfigured) return null;
  const supabase = await sessionClient();
  const { data } = await supabase.auth.getClaims();
  const id = data?.claims?.sub;
  return id ? { supabase, id } : null;
}

/** Una BO del usuario por su código (pública o privada); null si no es suya o no hay sesión. */
export async function getOwnBuildOrder(code: string): Promise<BuildOrderDetail | null> {
  if (!CODE_PATTERN.test(code)) return null;
  const user = await userClient();
  if (!user) return null;
  const { data, error } = await user.supabase
    .from("build_orders")
    .select(`${summaryColumns}, owner_id, updated_at, body, visibility`)
    .eq("code", code)
    .eq("owner_id", user.id)
    .maybeSingle();
  if (error) return failed("la BO propia", error.message, null);
  if (!data) return null;
  const row = data as unknown as Row & { owner_id: string; updated_at: string; body: Record<string, unknown>; visibility: Visibility };
  return { ...toSummary(row), owner_id: row.owner_id, updated_at: row.updated_at, body: row.body, visibility: row.visibility };
}

/** Los ids de las BO a las que está suscripto el usuario; null sin sesión. */
export async function mySubscriptionIds(): Promise<Set<string> | null> {
  const user = await userClient();
  if (!user) return null;
  const { data, error } = await user.supabase.from("subscriptions").select("build_order_id");
  if (error) return failed("las suscripciones", error.message, null);
  return new Set((data as { build_order_id: string }[]).map((r) => r.build_order_id));
}

/** Las BO a las que está suscripto el usuario y que siguen públicas, de la más reciente a la más vieja. */
export async function mySubscriptions(): Promise<BuildOrderSummary[]> {
  const user = await userClient();
  if (!user) return [];
  const { data, error } = await user.supabase
    .from("subscriptions")
    .select(`created_at, build_orders(${summaryColumns}, visibility)`)
    .order("created_at", { ascending: false });
  if (error) return failed("las suscripciones", error.message, []);
  // Una que pasó a privada no llega (la tapa la política de lectura) o llega privada si es del mismo usuario.
  return (data as unknown as { build_orders: (Row & { visibility: Visibility }) | null }[])
    .map((r) => r.build_orders)
    .filter((bo): bo is Row & { visibility: Visibility } => bo !== null && bo.visibility === "public")
    .map((row) => toSummary(row));
}

function failed<T>(what: string, message: string, fallback: T): T {
  console.error(`No se pudo leer ${what}: ${message}`);
  return fallback;
}
