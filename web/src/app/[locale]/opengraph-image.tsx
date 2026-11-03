import { getDictionary } from "@/i18n";
import { asLocale } from "@/i18n/locales";
import { OG_SIZE, ogCard } from "@/lib/ogImage";

export const alt = "AoE2 BO Coach";
export const size = OG_SIZE;
export const contentType = "image/png";

export default async function Image({ params }: { params: Promise<{ locale: string }> }) {
  const t = getDictionary(asLocale((await params).locale)).og;
  return ogCard({
    kicker: "Age of Empires II: Definitive Edition",
    title: "AoE2 BO Coach",
    facts: t.homeFacts,
    badges: t.homeBadges,
  });
}
