import { createServerClient } from "@supabase/ssr";
import { NextResponse, type NextRequest } from "next/server";
import { LOCALE_COOKIE, localePath } from "@/i18n/locales";
import { localeRouting, notFoundPath } from "@/i18n/routing";
import { matchupRouting } from "@/lib/matchups";
import { supabaseConfigured, supabaseKey, supabaseUrl } from "@/lib/supabase/env";

/**
 * Lo que se decide antes de armar una página: el idioma de la ruta (ver i18n/routing.ts) y renovar la sesión de
 * Supabase en cada navegación (los tokens vencen y se refrescan con las cookies), antes de que las páginas la lean. No
 * decide permisos: eso lo hacen las políticas por fila de la base.
 */
export async function proxy(request: NextRequest) {
  const to = (path: string) => {
    const url = request.nextUrl.clone();
    url.pathname = path;
    return url;
  };

  const route = localeRouting(request.nextUrl.pathname, request.cookies.get(LOCALE_COOKIE)?.value);
  if (route.kind === "redirect") return NextResponse.redirect(to(route.to), route.status);
  if (route.kind === "notFound") return NextResponse.rewrite(to(route.to), { status: 404 });

  // D10: /cruces/<pareja al revés> redirige con 308 y lo que no existe da 404 (la página, prerenderizada en partes,
  // ya no puede cambiar el estado cuando lo descubre).
  const matchup = matchupRouting(route.path);
  if (matchup && "redirect" in matchup) return NextResponse.redirect(to(localePath(route.locale, matchup.redirect)), 308);
  if (matchup) return NextResponse.rewrite(to(notFoundPath(route.locale)), { status: 404 });

  // El castellano no lleva prefijo en el navegador: por adentro va a /es/...
  const respond = () => (route.kind === "rewrite" ? NextResponse.rewrite(to(route.to), { request }) : NextResponse.next({ request }));
  let response = respond();
  if (!supabaseConfigured) return response;

  const supabase = createServerClient(supabaseUrl, supabaseKey, {
    cookies: {
      getAll: () => request.cookies.getAll(),
      setAll: (toSet, headers) => {
        for (const { name, value } of toSet) request.cookies.set(name, value);
        response = respond();
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
