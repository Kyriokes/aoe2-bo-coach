import type { NextRequest } from "next/server";
import { listBuildOrders } from "@/lib/data";
import { publishFromApp } from "./appApi";

/** GET /api/bo?q=&civ=&tag=&orden=votos|recientes: la biblioteca (sin el JSON de cada BO). */
export async function GET(request: NextRequest) {
  const params = request.nextUrl.searchParams;
  const items = await listBuildOrders({
    q: params.get("q") ?? undefined,
    civ: params.get("civ") ?? undefined,
    tag: params.get("tag") ?? undefined,
    sort: params.get("orden") === "recientes" ? "recientes" : "votos",
  });
  return Response.json({ items });
}

/** POST /api/bo: la app publica una BO nueva. Cuerpo: { build_order, feudal_click?, castle_click? }. */
export async function POST(request: NextRequest) {
  return publishFromApp(request, null);
}
