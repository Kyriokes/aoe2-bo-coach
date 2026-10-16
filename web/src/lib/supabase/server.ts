import "server-only";
import { createServerClient } from "@supabase/ssr";
import { createClient } from "@supabase/supabase-js";
import { cookies } from "next/headers";
import { supabaseConfigured, supabaseKey, supabaseUrl } from "./env";

/**
 * Cliente con la sesión del usuario (lee las cookies): para lo que depende de quién está logueado. Lee datos en
 * tiempo de pedido, así que los componentes que lo usan van dentro de <Suspense>.
 */
export async function sessionClient() {
  const store = await cookies();
  return createServerClient(supabaseUrl, supabaseKey, {
    cookies: {
      getAll: () => store.getAll(),
      setAll: (toSet) => {
        try {
          for (const { name, value, options } of toSet) store.set(name, value, options);
        } catch {
          // Desde un Server Component no se pueden escribir cookies: las renueva el proxy.
        }
      },
    },
  });
}

/** Cliente sin sesión, para lo público (la biblioteca): se puede cachear. null si no hay proyecto configurado. */
export function publicClient() {
  if (!supabaseConfigured) return null;
  return createClient(supabaseUrl, supabaseKey, { auth: { persistSession: false, autoRefreshToken: false } });
}
