import { NextResponse, type NextRequest } from "next/server";
import { asLocale, LOCALE_COOKIE, localePath, stripLocale } from "@/i18n/locales";

/** Solo rutas internas: que el selector no pueda mandar a otro sitio. */
function safePath(value: string | null) {
  return value && /^\/(?![/\\])/.test(value) ? value : "/";
}

/**
 * Si la ruta, ya sin el idioma, queda en este sitio. Se mira armando la dirección como lo hace el navegador: sacar el
 * idioma puede dejar "//otro.sitio", y al leer una dirección "\" vale "/" y los tabs y saltos de línea no están, así que
 * mirar el texto no alcanza.
 */
function staysInSite(path: string, base: string) {
  try {
    return new URL(path, base).origin === new URL(base).origin;
  } catch {
    return false;
  }
}

/** El selector de idioma: guarda la elección y vuelve a la misma página en ese idioma. */
export async function GET(request: NextRequest, { params }: RouteContext<"/idioma/[locale]">) {
  const locale = asLocale((await params).locale);
  const { path } = stripLocale(safePath(request.nextUrl.searchParams.get("volver")));
  const target = localePath(locale, staysInSite(path, request.url) ? path : "/");
  const response = NextResponse.redirect(new URL(target, request.url), 307);
  response.cookies.set(LOCALE_COOKIE, locale, { path: "/", maxAge: 60 * 60 * 24 * 365, sameSite: "lax" });
  return response;
}
