import { fill, getDictionary } from "@/i18n";
import { asLocale } from "@/i18n/locales";
import { formatClock } from "@/lib/buildOrder";
import { getBuildOrder } from "@/lib/data";
import { OG_SIZE, ogCard } from "@/lib/ogImage";
import { byline, civLabel } from "@/lib/share";

export const alt = "AoE2 BO Coach";
export const size = OG_SIZE;
export const contentType = "image/png";

/** La tarjeta de una BO al compartir su enlace: nombre, civ, autor, clics de edad y cantidad de pasos. */
export default async function Image({ params }: { params: Promise<{ locale: string; code: string }> }) {
  const { locale: raw, code } = await params;
  const locale = asLocale(raw);
  const t = getDictionary(locale);
  const bo = await getBuildOrder(code);
  if (!bo) {
    return ogCard({ kicker: "AoE2 BO Coach", title: t.og.notFoundTitle, facts: t.og.notFoundFacts });
  }

  const steps = Array.isArray(bo.body.steps) ? bo.body.steps.length : 0;
  const badges = [
    bo.feudal_click !== null ? fill(t.bo.feudal, { time: formatClock(bo.feudal_click) }) : null,
    bo.castle_click !== null ? fill(t.bo.castle, { time: formatClock(bo.castle_click) }) : null,
    steps > 0 ? fill(t.og.stepsBadge, { n: steps }) : null,
    fill(bo.votes === 1 ? t.bo.voteOne : t.bo.voteOther, { n: bo.votes }),
  ].filter((badge) => badge !== null);

  return ogCard({
    kicker: fill(t.og.kickerCode, { code: bo.code }),
    title: bo.name,
    facts: `${civLabel(bo.civ, locale)} · ${fill(t.bo.by, { author: byline(bo, locale) })}`,
    badges,
  });
}
