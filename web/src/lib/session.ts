import "server-only";
import { supabaseConfigured } from "./supabase/env";
import { sessionClient } from "./supabase/server";

export interface CurrentUser {
  id: string;
  /** null si el perfil no tiene nombre: lo completa quien lo muestra, en su idioma. */
  name: string | null;
  avatarUrl: string | null;
}

/**
 * El usuario logueado (o null). Lee las cookies, así que se usa dentro de <Suspense>. Los permisos reales los
 * ponen las políticas de la base: esto es para mostrar.
 */
export async function getCurrentUser(): Promise<CurrentUser | null> {
  if (!supabaseConfigured) return null;
  const supabase = await sessionClient();
  const { data } = await supabase.auth.getClaims();
  const id = data?.claims?.sub;
  if (!id) return null;
  const { data: profile } = await supabase.from("profiles").select("display_name, avatar_url").eq("id", id).maybeSingle();
  return {
    id,
    name: (profile?.display_name as string | undefined) ?? null,
    avatarUrl: (profile?.avatar_url as string | null | undefined) ?? null,
  };
}
