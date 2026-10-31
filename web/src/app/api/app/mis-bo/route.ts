import { appRpc } from "../../bo/appApi";

/**
 * GET /api/app/mis-bo: { account, items }. El nombre de la cuenta del token ("Conectada como ...") y sus BO (también
 * privadas) más sus suscriptas públicas, sin los pasos (la app las baja de a una con GET /api/bo/<código> cuando las
 * va a usar).
 */
export async function GET(request: Request) {
  const result = await appRpc<{ account: string | null; items: unknown[] | null } | null>(request, "app_my_build_orders", {});
  if ("response" in result) return result.response;
  return Response.json({ account: result.data?.account ?? "", items: result.data?.items ?? [] });
}
