import type { NextRequest } from "next/server";
import { CODE_PATTERN, getBuildOrder } from "@/lib/data";
import { appRpc, bearerToken, error, publishFromApp } from "../appApi";

/**
 * GET /api/bo/<código>: una BO con su JSON, para "Importar desde la web" y los enlaces bocoach://import/<código>.
 * Las públicas salen del caché; con el token de la app (Authorization: Bearer) también las privadas del dueño.
 */
export async function GET(request: NextRequest, context: RouteContext<"/api/bo/[code]">) {
  const { code } = await context.params;
  const missing = () => error(404, `No hay ninguna build order con el código ${code}.`);
  const bo = await getBuildOrder(code.toLowerCase());
  if (!bo) {
    // Una privada solo con el token de su dueño. Sin token, lo mismo que una que no existe.
    if (!bearerToken(request) || !CODE_PATTERN.test(code.toLowerCase())) return missing();
    const own = await appRpc<Record<string, unknown> | null>(request, "app_build_order", { p_code: code.toLowerCase() });
    if ("response" in own) return own.response;
    return own.data ? Response.json(own.data) : missing();
  }
  return Response.json({
    code: bo.code,
    name: bo.name,
    civ: bo.civ,
    tags: bo.tags,
    // "author" es la cuenta que la subió: la app la muestra al pedir confirmación y el autor del JSON lo escribe cualquiera.
    author: bo.uploader,
    guide_author: bo.author,
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
