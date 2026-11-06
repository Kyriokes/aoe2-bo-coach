import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { BuildOrderView } from "@/components/BuildOrderView";
import { JsonLd } from "@/components/JsonLd";
import { OpenInApp } from "@/components/OpenInApp";
import { OwnerActions } from "@/components/OwnerActions";
import { SubscribeButton } from "@/components/SubscribeButton";
import { VoteButton, Votes } from "@/components/VoteButton";
import { StepList, stepsOf, type ListedStep } from "@/components/StepList";
import { fill, getDictionary } from "@/i18n";
import Link from "@/i18n/Link";
import { asLocale, LANG_TAG, localeAlternates, localePath, OG_LOCALE, type Locale } from "@/i18n/locales";
import { i18n } from "@/i18n/server";
import { getBuildOrder } from "@/lib/data";
import { getMatchups } from "@/lib/matchupsServer";
import { civsWithData } from "@/lib/matchupView";
import { GAME } from "@/lib/jsonLd";
import { getOwnBuildOrder, mySubscriptionIds } from "@/lib/ownData";
import { getCurrentUser } from "@/lib/session";
import { linkify } from "@/lib/linkify";
import { byline, civLabel, shareDescription, SITE_NAME } from "@/lib/share";
import { siteUrl } from "@/lib/site";
import { describeStep } from "@/lib/stepText";
import { sessionClient } from "@/lib/supabase/server";

/** D10: si la civ tiene estadísticas reales de cruces (para enlazar a /cruces/<civ>). */
function hasMatchups(civ: string) {
  const data = getMatchups();
  return civ !== "any" && data !== null && civsWithData(data).includes(civ.toLowerCase());
}

/** "Scouts Feudal · build order de Britanos": el título para los buscadores (la página muestra solo el nombre). */
function pageTitle(bo: { name: string; civ: string }, locale: Locale) {
  const t = getDictionary(locale).bo;
  return bo.civ === "any" ? fill(t.titleAnyCiv, { name: bo.name }) : fill(t.titleOfCiv, { name: bo.name, civ: civLabel(bo.civ, locale) });
}

export async function generateMetadata({ params }: PageProps<"/[locale]/bo/[code]">): Promise<Metadata> {
  const { code, locale: raw } = await params;
  const locale = asLocale(raw);
  const t = getDictionary(locale);
  const bo = await getBuildOrder(code);
  if (!bo) {
    // Una privada propia lleva su nombre, pero nunca se indexa.
    const own = await getOwnBuildOrder(code);
    return { title: own ? own.name : t.bo.notFoundTitle, robots: { index: false } };
  }
  // Lo que muestran Discord, WhatsApp o los buscadores al compartir el enlace (la imagen sale de opengraph-image).
  const description = shareDescription(bo, 200, locale);
  const path = `/bo/${bo.code}`;
  const title = pageTitle(bo, locale);
  const author = bo.author || t.user.anonymous;
  return {
    title,
    description,
    alternates: localeAlternates(locale, path),
    authors: [{ name: author }],
    openGraph: {
      type: "article",
      siteName: SITE_NAME,
      locale: OG_LOCALE[locale],
      title,
      description,
      url: localePath(locale, path),
      publishedTime: bo.created_at,
      modifiedTime: bo.updated_at,
      authors: [author],
      tags: bo.tags,
    },
    twitter: { card: "summary_large_image", title, description },
  };
}

/** La BO para los buscadores: una guía paso a paso (HowTo) y el camino Inicio › Biblioteca › BO. */
function structuredData(bo: NonNullable<Awaited<ReturnType<typeof getBuildOrder>>>, steps: ListedStep[], locale: Locale) {
  const t = getDictionary(locale);
  const base = siteUrl();
  const url = `${base}${localePath(locale, `/bo/${bo.code}`)}`;
  return {
    "@context": "https://schema.org",
    "@graph": [
      {
        "@type": "HowTo",
        "@id": `${url}#bo`,
        name: bo.name,
        description: shareDescription(bo, 500, locale),
        url,
        image: `${url}/opengraph-image`,
        inLanguage: locale,
        datePublished: bo.created_at,
        dateModified: bo.updated_at,
        author: { "@type": "Person", name: bo.author || t.user.anonymous },
        about: GAME,
        keywords: [civLabel(bo.civ, locale), ...bo.tags].join(", "),
        step: steps.slice(0, 100).map((step, i) => ({
          "@type": "HowToStep",
          position: i + 1,
          text: typeof step.note === "string" && step.note ? `${describeStep(step, locale)}. ${step.note}` : describeStep(step, locale),
        })),
      },
      {
        "@type": "BreadcrumbList",
        itemListElement: [
          { "@type": "ListItem", position: 1, name: SITE_NAME, item: `${base}${locale === "es" ? "" : localePath(locale, "/")}` },
          { "@type": "ListItem", position: 2, name: t.bo.jsonLdLibrary, item: `${base}${localePath(locale, "/biblioteca")}` },
          { "@type": "ListItem", position: 3, name: bo.name, item: url },
        ],
      },
    ],
  };
}

export default async function BuildOrderPage({ params }: PageProps<"/[locale]/bo/[code]">) {
  const { t } = await i18n();
  return (
    <Suspense fallback={<p className="text-sm text-faint">{t.bo.loading}</p>}>
      <Detail params={params} />
    </Suspense>
  );
}

async function Detail({ params }: { params: PageProps<"/[locale]/bo/[code]">["params"] }) {
  const { code } = await params;
  const { locale, t: dict } = await i18n();
  const t = dict.bo;
  // Las públicas salen del caché; una privada solo la ve su dueño, leída con la sesión.
  const bo = (await getBuildOrder(code)) ?? (await getOwnBuildOrder(code));
  if (!bo) notFound();
  const isPrivate = bo.visibility === "private";
  const json = JSON.stringify(bo.body);
  const steps = stepsOf(bo.body);

  return (
    <article className="flex flex-col gap-6">
      {isPrivate ? null : <JsonLd data={structuredData(bo, steps, locale)} />}
      <header className="flex flex-col gap-2">
        <nav aria-label={t.breadcrumb} className="text-sm">
          <Link href="/biblioteca" className="text-faint hover:text-amber">
            {t.backToLibrary}
          </Link>
          {bo.civ !== "any" ? (
            <>
              <span className="text-faint"> · </span>
              <Link href={`/biblioteca?civ=${encodeURIComponent(bo.civ)}`} className="text-faint hover:text-amber">
                {fill(t.moreOf, { civ: civLabel(bo.civ, locale) })}
              </Link>
            </>
          ) : null}
          {hasMatchups(bo.civ) ? (
            <>
              <span className="text-faint"> · </span>
              <Link href={`/cruces/${bo.civ.toLowerCase()}`} className="text-faint hover:text-amber">
                {t.matchupsLink}
              </Link>
            </>
          ) : null}
        </nav>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h1 className="text-2xl font-semibold">
              {bo.name}
              {isPrivate ? (
                <span className="ml-2 rounded bg-panel-2 px-2 py-0.5 align-middle text-xs font-normal text-muted" title={t.privateTitle}>
                  {t.private}
                </span>
              ) : null}
            </h1>
            <p className="text-sm text-faint">
              {civLabel(bo.civ, locale)} · {fill(t.by, { author: byline(bo, locale) })} · {fill(t.updated, { date: new Date(bo.updated_at).toLocaleDateString(LANG_TAG[locale]) })}
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            {isPrivate ? null : (
              <Suspense fallback={null}>
                <Subscribe buildOrderId={bo.id} code={bo.code} ownerId={bo.owner_id} />
              </Suspense>
            )}
            {isPrivate ? null : (
              <Suspense fallback={<span className="button"><Votes votes={bo.votes} /></span>}>
                <Vote buildOrderId={bo.id} code={bo.code} votes={bo.votes} />
              </Suspense>
            )}
          </div>
        </div>
        {bo.description ? (
          <p className="max-w-3xl whitespace-pre-line text-sm text-muted">
            {/* La guía original de una BO importada ("Fuente: https://...") como enlace; nofollow porque la escribe cualquiera. */}
            {linkify(bo.description).map((part, i) =>
              part.href ? (
                <a key={i} href={part.href} target="_blank" rel="nofollow ugc noopener noreferrer" className="break-all text-amber hover:underline">
                  {part.text}
                </a>
              ) : (
                part.text
              ),
            )}
          </p>
        ) : null}
        {bo.tags.length > 0 ? (
          <ul className="flex flex-wrap gap-1.5">
            {bo.tags.map((tag) => (
              <li key={tag}>
                <Link href={`/biblioteca?tag=${encodeURIComponent(tag)}`} className="rounded bg-panel-2 px-2 py-0.5 text-xs text-muted hover:text-amber">
                  {tag}
                </Link>
              </li>
            ))}
          </ul>
        ) : null}
      </header>

      {isPrivate ? null : <OpenInApp code={bo.code} fileName={bo.name} />}
      <Suspense fallback={null}>
        <Owner ownerId={bo.owner_id} code={bo.code} />
      </Suspense>
      <section aria-labelledby="pasos" className="flex flex-col gap-3">
        <h2 id="pasos" className="text-lg font-semibold">
          {t.steps}
          {steps.length > 0 ? ` (${steps.length})` : ""}
        </h2>
        <BuildOrderView json={json} fallback={<StepList steps={steps} locale={locale} />} />
      </section>
    </article>
  );
}

/** Si el usuario ya votó: lee la sesión, así que va dentro de <Suspense>. */
async function Vote({ buildOrderId, code, votes }: { buildOrderId: string; code: string; votes: number }) {
  const user = await getCurrentUser();
  let voted = false;
  if (user) {
    const supabase = await sessionClient();
    const { data } = await supabase.from("votes").select("build_order_id").eq("build_order_id", buildOrderId).eq("user_id", user.id).maybeSingle();
    voted = data !== null;
  }
  return <VoteButton buildOrderId={buildOrderId} code={code} votes={votes} voted={voted} loggedIn={user !== null} />;
}

/** "Usar en el coach": lee la sesión y las suscripciones, así que va dentro de <Suspense>. */
async function Subscribe({ buildOrderId, code, ownerId }: { buildOrderId: string; code: string; ownerId: string }) {
  const [user, subscriptions] = await Promise.all([getCurrentUser(), mySubscriptionIds()]);
  return (
    <SubscribeButton
      buildOrderId={buildOrderId}
      code={code}
      subscribed={subscriptions?.has(buildOrderId) ?? false}
      loggedIn={user !== null}
      own={user?.id === ownerId}
    />
  );
}

async function Owner({ ownerId, code }: { ownerId: string; code: string }) {
  const user = await getCurrentUser();
  return user?.id === ownerId ? <OwnerActions code={code} /> : null;
}
