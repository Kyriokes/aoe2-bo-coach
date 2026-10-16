import type { NextRequest } from "next/server";
import { getBuildOrder } from "@/lib/data";
import { error, publishFromApp } from "../appApi";

/** GET /api/bo/<código>: una BO con su JSON, para "Importar desde la web" y los enlaces bocoach://import/<código>. */
export async function GET(_request: NextRequest, context: RouteContext<"/api/bo/[code]">) {
  const { code } = await context.params;
  const bo = await getBuildOrder(code.toLowerCase());
  if (!bo) return error(404, `No hay ninguna build order con el código ${code}.`);
  return Response.json({
    code: bo.code,
    name: bo.name,
    civ: bo.civ,
    tags: bo.tags,
    author: bo.author,
    votes: bo.votes,
    feudal_click: bo.feudal_click,
    castle_click: bo.castle_click,
    updated_at: bo.updated_at,
    build_order: bo.body,
  });
}

/** PUT /api/bo/<código>: la app reemplaza una BO propia (mismo cuerpo que POST /api/bo). */
export async function PUT(request: NextRequest, context: RouteContext<"/api/bo/[code]">) {
  const { code } = await context.params;
  return publishFromApp(request, code.toLowerCase());
}
