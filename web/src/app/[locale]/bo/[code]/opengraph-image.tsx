import { formatClock } from "@/lib/buildOrder";
import { getBuildOrder } from "@/lib/data";
import { OG_SIZE, ogCard } from "@/lib/ogImage";
import { byline, civLabel } from "@/lib/share";

export const alt = "Vista previa de la build order";
export const size = OG_SIZE;
export const contentType = "image/png";

/** La tarjeta de una BO al compartir su enlace: nombre, civ, autor, clics de edad y cantidad de pasos. */
export default async function Image({ params }: { params: Promise<{ code: string }> }) {
  const { code } = await params;
  const bo = await getBuildOrder(code);
  if (!bo) {
    return ogCard({ kicker: "AoE2 BO Coach", title: "Build order no encontrada", facts: "Puede que se haya borrado" });
  }

  const steps = Array.isArray(bo.body.steps) ? bo.body.steps.length : 0;
  const badges = [
    bo.feudal_click !== null ? `Feudal ${formatClock(bo.feudal_click)}` : null,
    bo.castle_click !== null ? `Castillos ${formatClock(bo.castle_click)}` : null,
    steps > 0 ? `${steps} pasos` : null,
    `${bo.votes} ${bo.votes === 1 ? "voto" : "votos"}`,
  ].filter((badge) => badge !== null);

  return ogCard({
    kicker: `AoE2 BO Coach · código ${bo.code}`,
    title: bo.name,
    facts: `${civLabel(bo.civ)} · por ${byline(bo)}`,
    badges,
  });
}
