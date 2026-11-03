import { NextResponse, type NextRequest } from "next/server";
import { asLocale, LOCALE_COOKIE, localePath, stripLocale } from "@/i18n/locales";

/** Solo rutas internas: que el selector no pueda mandar a otro sitio. */
function safePath(value: string | null) {
  return value && /^\/(?![/\\])/.test(value) ? value : "/";
}

/** El selector de idioma: guarda la elección y vuelve a la misma página en ese idioma. */
export async function GET(request: NextRequest, { params }: RouteContext<"/idioma/[locale]">) {
  const locale = asLocale((await params).locale);
  const { path } = stripLocale(safePath(request.nextUrl.searchParams.get("volver")));
  const response = NextResponse.redirect(new URL(localePath(locale, path), request.url), 307);
  response.cookies.set(LOCALE_COOKIE, locale, { path: "/", maxAge: 60 * 60 * 24 * 365, sameSite: "lax" });
  return response;
}
