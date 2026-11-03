import type { Metadata } from "next";
import { Suspense } from "react";
import { ConnectAppButton } from "@/components/ConnectAppButton";
import { TokenManager, type TokenRow } from "@/components/TokenManager";
import { UnsubscribeButton } from "@/components/UnsubscribeButton";
import { VisibilityToggle } from "@/components/VisibilityToggle";
import { fill, getDictionary, rich } from "@/i18n";
import Link from "@/i18n/Link";
import { asLocale, localePath } from "@/i18n/locales";
import { i18n } from "@/i18n/server";
import { formatClock, type Visibility } from "@/lib/buildOrder";
import { byline } from "@/lib/share";
import { mySubscriptions } from "@/lib/ownData";
import { getCurrentUser } from "@/lib/session";
import { sessionClient } from "@/lib/supabase/server";

export async function generateMetadata({ params }: PageProps<"/[locale]/cuenta">): Promise<Metadata> {
  return { title: getDictionary(asLocale((await params).locale)).account.title, robots: { index: false, follow: false } };
}

export default async function AccountPage() {
  const { t } = await i18n();
  return (
    <Suspense fallback={<p className="text-sm text-faint">{t.account.loading}</p>}>
      <Account />
    </Suspense>
  );
}

async function Account() {
  const { locale, t: dict } = await i18n();
  const t = dict.account;
  const amber = (href: string) => {
    function AmberLink(text: string) {
      return (
        <Link href={href} className="text-amber hover:underline">
          {text}
        </Link>
      );
    }
    return AmberLink;
  };
  const user = await getCurrentUser();
  if (!user) {
    return <p className="text-sm text-muted">{rich(t.needLogin, { link: amber(`/login?volver=${localePath(locale, "/cuenta")}`) })}</p>;
  }

  const supabase = await sessionClient();
  const [{ data: mine }, { data: tokens }, subscriptions] = await Promise.all([
    supabase
      .from("build_orders")
      .select("code, name, votes, feudal_click, updated_at, visibility")
      .eq("owner_id", user.id)
      .order("updated_at", { ascending: false }),
    supabase.from("app_tokens").select("id, label, created_at, last_used_at").order("created_at", { ascending: false }),
    mySubscriptions(),
  ]);
  const own = (mine ?? []) as {
    code: string;
    name: string;
    votes: number;
    feudal_click: number | null;
    updated_at: string;
    visibility: Visibility;
  }[];

  return (
    <div className="flex flex-col gap-8">
      <h1 className="text-2xl font-semibold">{fill(t.hello, { name: user.name ?? dict.user.fallbackName })}</h1>

      <section className="flex flex-col gap-3">
        <h2 className="text-lg font-semibold">{t.yours}</h2>
        {own.length === 0 ? (
          <p className="text-sm text-muted">{rich(t.noneYet, { link: amber("/subir") })}</p>
        ) : (
          <ul className="divide-y divide-line rounded-md border border-line bg-panel">
            {own.map((bo) => (
              <li key={bo.code} className="flex items-center gap-3 px-3 py-2 text-sm">
                <Link href={`/bo/${bo.code}`} className="flex-1 hover:text-amber">
                  {bo.name}
                </Link>
                <span className="text-xs text-faint">{bo.feudal_click !== null ? fill(dict.bo.feudal, { time: formatClock(bo.feudal_click) }) : ""}</span>
                <span className="font-mono text-xs text-amber">{bo.code}</span>
                <span className="text-xs text-faint">▲ {bo.votes}</span>
                <VisibilityToggle code={bo.code} visibility={bo.visibility} />
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className="flex flex-col gap-3">
        <h2 className="text-lg font-semibold">{t.inCoach}</h2>
        <p className="max-w-2xl text-sm text-muted">{t.inCoachText}</p>
        {subscriptions.length === 0 ? (
          <p className="text-sm text-muted">{rich(t.noSubscriptions, { link: amber("/biblioteca") })}</p>
        ) : (
          <ul className="divide-y divide-line rounded-md border border-line bg-panel">
            {subscriptions.map((bo) => (
              <li key={bo.code} className="flex items-center gap-3 px-3 py-2 text-sm">
                <Link href={`/bo/${bo.code}`} className="flex-1 hover:text-amber">
                  {bo.name}
                </Link>
                <span className="text-xs text-faint">{fill(dict.bo.by, { author: byline(bo, locale) })}</span>
                <UnsubscribeButton buildOrderId={bo.id} />
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className="flex flex-col gap-3">
        <h2 className="text-lg font-semibold">{t.connectTitle}</h2>
        <p className="max-w-2xl text-sm text-muted">{t.connectText}</p>
        <ConnectAppButton />
        <TokenManager tokens={(tokens ?? []) as TokenRow[]} />
      </section>
    </div>
  );
}
