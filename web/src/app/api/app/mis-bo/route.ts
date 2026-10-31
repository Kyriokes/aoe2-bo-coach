import { appRpc } from "../../bo/appApi";

/**
 * GET /api/app/mis-bo: las BO del dueño del token (también privadas) y sus suscriptas públicas, sin los pasos (la
 * app las baja de a una con GET /api/bo/<código> cuando las va a usar).
 */
export async function GET(request: Request) {
  const result = await appRpc<unknown[]>(request, "app_my_build_orders", {});
  if ("response" in result) return result.response;
  return Response.json({ items: result.data ?? [] });
}
