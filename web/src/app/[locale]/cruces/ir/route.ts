import { NextResponse, type NextRequest } from "next/server";
import { asLocale, localePath } from "@/i18n/locales";
import { pairSlug, parseSlug } from "@/lib/matchups";

/** El formulario de /cruces (dos civs) manda acá: redirige al cruce, o de vuelta a /cruces si falta algo. */
export async function GET(request: NextRequest, { params }: RouteContext<"/[locale]/cruces/ir">) {
  const locale = asLocale((await params).locale);
  const a = request.nextUrl.searchParams.get("a")?.trim().toLowerCase() ?? "";
  const b = request.nextUrl.searchParams.get("b")?.trim().toLowerCase() ?? "";
  const parsed = a && b ? parseSlug(`${a}-vs-${b}`) : null;
  const target = parsed && "a" in parsed ? `/cruces/${pairSlug(parsed.a, parsed.b)}` : "/cruces";
  return NextResponse.redirect(new URL(localePath(locale, target), request.url), 303);
}
