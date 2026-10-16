import { createBrowserClient } from "@supabase/ssr";
import { supabaseKey, supabaseUrl } from "./env";

/** Cliente para componentes de cliente (iniciar sesión con Discord o Google). */
export function browserClient() {
  return createBrowserClient(supabaseUrl, supabaseKey);
}
