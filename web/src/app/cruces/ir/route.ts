import { NextResponse, type NextRequest } from "next/server";
import { pairSlug, parseSlug } from "@/lib/matchups";

/** El formulario de /cruces (dos civs) manda acá: redirige al cruce, o de vuelta a /cruces si falta algo. */
export function GET(request: NextRequest) {
  const a = request.nextUrl.searchParams.get("a")?.trim().toLowerCase() ?? "";
  const b = request.nextUrl.searchParams.get("b")?.trim().toLowerCase() ?? "";
  const parsed = a && b ? parseSlug(`${a}-vs-${b}`) : null;
  const target = parsed && "a" in parsed ? `/cruces/${pairSlug(parsed.a, parsed.b)}` : "/cruces";
  return NextResponse.redirect(new URL(target, request.url), 303);
}
