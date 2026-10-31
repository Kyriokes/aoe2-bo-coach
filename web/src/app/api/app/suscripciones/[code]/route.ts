import type { NextRequest } from "next/server";
import { CODE_PATTERN } from "@/lib/data";
import { appRpc, error } from "../../../bo/appApi";

const notFound = (code: string) => error(404, `No hay ninguna build order pública con el código ${code}.`);

/** POST /api/app/suscripciones/<código>: suma una BO pública a la app. { code, own } (own: es del dueño del token). */
export async function POST(request: NextRequest, context: RouteContext<"/api/app/suscripciones/[code]">) {
  const code = (await context.params).code.toLowerCase();
  if (!CODE_PATTERN.test(code)) return notFound(code);
  const result = await appRpc<string>(request, "app_subscribe", { p_code: code });
  if ("response" in result) return result.response;
  return Response.json({ code, own: result.data === "own" });
}

/** DELETE /api/app/suscripciones/<código>: la saca (sin error si no estaba). */
export async function DELETE(request: NextRequest, context: RouteContext<"/api/app/suscripciones/[code]">) {
  const code = (await context.params).code.toLowerCase();
  if (!CODE_PATTERN.test(code)) return notFound(code);
  const result = await appRpc<null>(request, "app_unsubscribe", { p_code: code });
  if ("response" in result) return result.response;
  return new Response(null, { status: 204 });
}
