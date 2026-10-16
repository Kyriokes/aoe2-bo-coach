import { createServerClient } from "@supabase/ssr";
import { NextResponse, type NextRequest } from "next/server";
import { supabaseConfigured, supabaseKey, supabaseUrl } from "@/lib/supabase/env";

/**
 * Renueva la sesión de Supabase en cada navegación (los tokens vencen y se refrescan con las cookies), antes de
 * que las páginas la lean. No decide permisos: eso lo hacen las políticas por fila de la base.
 */
export async function proxy(request: NextRequest) {
  let response = NextResponse.next({ request });
  if (!supabaseConfigured) return response;

  const supabase = createServerClient(supabaseUrl, supabaseKey, {
    cookies: {
      getAll: () => request.cookies.getAll(),
      setAll: (toSet, headers) => {
        for (const { name, value } of toSet) request.cookies.set(name, value);
        response = NextResponse.next({ request });
        for (const { name, value, options } of toSet) response.cookies.set(name, value, options);
        for (const [key, value] of Object.entries(headers ?? {})) response.headers.set(key, value);
      },
    },
  });
  await supabase.auth.getClaims();
  return response;
}

export const config = {
  // Todo menos archivos estáticos, el Core en WebAssembly y la API de la app (que usa tokens, no cookies).
  matcher: ["/((?!_next/static|_next/image|favicon.ico|coach/|api/).*)"],
};
