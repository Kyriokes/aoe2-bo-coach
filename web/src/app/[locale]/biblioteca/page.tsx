import type { Metadata } from "next";
import { Suspense } from "react";
import { BoCard } from "@/components/BoCard";
import { fill, getDictionary, rich } from "@/i18n";
import Link from "@/i18n/Link";
import { asLocale, localeAlternates, localePath, OG_LOCALE, type Locale } from "@/i18n/locales";
import { i18n } from "@/i18n/server";
import { listBuildOrders, listFacets, type LibraryQuery } from "@/lib/data";
import { knownCiv, knownTag, libraryHref, libraryPageFor, pageNumber } from "@/lib/filters";
import { mySubscriptionIds } from "@/lib/ownData";
import { getCurrentUser } from "@/lib/session";
import { civLabel, SITE_NAME } from "@/lib/share";
import { supabaseConfigured } from "@/lib/supabase/env";

/** "de Britanos", "para cualquier civ" o null sin filtro de civ. */
function civSubject(civ: string | undefined, locale: Locale) {
  if (!civ) return null;
  const t = getDictionary(locale).library;
  return civ === "any" ? t.subjectAny : fill(t.subjectCiv, { civ: civLabel(civ, locale) });
}

/**
 * Cada civ y cada etiqueta conocida es una página para los buscadores ("Build orders de Britanos"); las búsquedas por
 * texto, el orden y lo inventado no: se pueden seguir, pero no se indexan, y la canónica es la de la civ o la etiqueta.
 */
export async function generateMetadata({ params, searchParams }: PageProps<"/[locale]/biblioteca">): Promise<Metadata> {
  const locale = asLocale((await params).locale);
  const t = getDictionary(locale).library;
  const query = await readQuery(searchParams);
  const { civ, tag, noindex } = libraryPageFor(query);
  const subject = civSubject(civ, locale);
  const title = subject ? fill(t.titleSubject, { subject }) : tag ? fill(t.titleTag, { tag }) : t.title;
  const description = subject ? fill(t.descriptionSubject, { subject }) : t.description;
  const path = libraryHref(civ ? { civ } : tag ? { tag } : {}, query.page);
  return {
    title,
    description,
    alternates: localeAlternates(locale, path),
    openGraph: { siteName: SITE_NAME, locale: OG_LOCALE[locale], type: "website", title, description, url: localePath(locale, path) },
    robots: noindex ? { index: false, follow: true } : undefined,
  };
}

type SearchParams = PageProps<"/[locale]/biblioteca">["searchParams"];

function first(value: string | string[] | undefined) {
  return (Array.isArray(value) ? value[0] : value)?.trim() || undefined;
}

async function readQuery(searchParams: SearchParams): Promise<LibraryQuery> {
  const params = await searchParams;
  return {
    q: first(params.q),
    civ: first(params.civ),
    tag: first(params.tag),
    sort: first(params.orden) === "recientes" ? "recientes" : "votos",
    page: pageNumber(first(params.pagina)),
  };
}

export default async function LibraryPage({ searchParams }: PageProps<"/[locale]/biblioteca">) {
  const { locale, t: dict } = await i18n();
  const t = dict.library;
  return (
    <div className="flex flex-col gap-6">
      <section>
        <Suspense fallback={<h1 className="text-2xl font-semibold">{t.title}</h1>}>
          <Title searchParams={searchParams} locale={locale} />
        </Suspense>
        <p className="mt-1 max-w-2xl text-sm text-muted">
          {rich(t.intro, {
            link: (text) => (
              <Link href="/subir" className="text-amber hover:underline">
                {text}
              </Link>
            ),
          })}
        </p>
      </section>
      <Suspense fallback={<div className="h-10" />}>
        <Filters searchParams={searchParams} locale={locale} />
      </Suspense>
      <Suspense fallback={<p className="text-sm text-faint">{t.loading}</p>}>
        <Results searchParams={searchParams} locale={locale} />
      </Suspense>
    </div>
  );
}

async function Title({ searchParams, locale }: { searchParams: SearchParams; locale: Locale }) {
  const t = getDictionary(locale).library;
  const subject = civSubject(knownCiv((await readQuery(searchParams)).civ), locale);
  return <h1 className="text-2xl font-semibold">{subject ? fill(t.titleSubject, { subject }) : t.title}</h1>;
}

async function Filters({ searchParams, locale }: { searchParams: SearchParams; locale: Locale }) {
  const t = getDictionary(locale).library;
  const [query, facets] = await Promise.all([readQuery(searchParams), listFacets()]);
  return (
    <form className="flex flex-wrap items-center gap-2" role="search">
      <input
        name="q"
        type="search"
        defaultValue={query.q}
        placeholder={t.searchPlaceholder}
        aria-label={t.searchLabel}
        className="field min-w-56 flex-1"
      />
      <select name="civ" defaultValue={knownCiv(query.civ) ?? ""} className="field" aria-label={t.civLabel}>
        <option value="">{t.allCivs}</option>
        {[...facets.civs].sort((a, b) => civLabel(a, locale).localeCompare(civLabel(b, locale), locale)).map((civ) => (
          <option key={civ} value={civ}>
            {civLabel(civ, locale)}
          </option>
        ))}
      </select>
      <select name="tag" defaultValue={knownTag(query.tag) ?? ""} className="field" aria-label={t.tagLabel}>
        <option value="">{t.allTags}</option>
        {facets.tags.map((tag) => (
          <option key={tag} value={tag}>
            {tag}
          </option>
        ))}
      </select>
      <select name="orden" defaultValue={query.sort} className="field" aria-label={t.sortLabel}>
        <option value="votos">{t.sortVotes}</option>
        <option value="recientes">{t.sortRecent}</option>
      </select>
      <button type="submit" className="button-primary">
        {t.search}
      </button>
    </form>
  );
}

async function Results({ searchParams, locale }: { searchParams: SearchParams; locale: Locale }) {
  const t = getDictionary(locale).library;
  const query = await readQuery(searchParams);
  if (!supabaseConfigured) {
    return <p className="text-sm text-faint">{t.notConnected}</p>;
  }
  // La página sale del caché; la sesión y las suscripciones, no (solo para el botón "Usar en el coach").
  const [{ items, hasMore }, user, subscriptions] = await Promise.all([listBuildOrders(query), getCurrentUser(), mySubscriptionIds()]);
  const page = query.page ?? 1;
  if (items.length === 0) {
    return (
      <p className="text-sm text-faint">
        {page > 1
          ? rich(fill(t.emptyPage, { page }), {
              link: (text) => (
                <Link href={libraryHref(query)} className="text-amber hover:underline">
                  {text}
                </Link>
              ),
            })
          : t.empty}
      </p>
    );
  }
  return (
    <>
      <div className="grid gap-3 sm:grid-cols-2">
        {items.map((bo) => (
          <BoCard
            key={bo.code}
            bo={bo}
            locale={locale}
            subscription={user ? { subscribed: subscriptions?.has(bo.id) ?? false, own: bo.owner_id === user.id } : undefined}
          />
        ))}
      </div>
      {page > 1 || hasMore ? (
        <nav aria-label={t.pagesLabel} className="flex items-center justify-between gap-3 text-sm">
          {page > 1 ? (
            <Link href={libraryHref(query, page - 1)} className="button" rel="prev">
              {t.previous}
            </Link>
          ) : (
            <span />
          )}
          <span className="text-faint">{fill(t.page, { page })}</span>
          {hasMore ? (
            <Link href={libraryHref(query, page + 1)} className="button" rel="next">
              {t.next}
            </Link>
          ) : (
            <span />
          )}
        </nav>
      ) : null}
    </>
  );
}
