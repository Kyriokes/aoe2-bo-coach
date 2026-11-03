import type { Metadata } from "next";
import { notFound, permanentRedirect } from "next/navigation";
import { MatchupDuration } from "@/components/MatchupDuration";
import { MatchupRivals, type Choice } from "@/components/MatchupRivals";
import { fill, getDictionary } from "@/i18n";
import Link from "@/i18n/Link";
import { asLocale, LANG_TAG, localeAlternates, localePath, OG_LOCALE, type Locale } from "@/i18n/locales";
import { i18n } from "@/i18n/server";
import { chanceColor, chanceText, durationLabel, eloLabel, isEven, mapName, oneVsOne, pairSlug, parseSlug, type MatchupData } from "@/lib/matchups";
import { getMatchups } from "@/lib/matchupsServer";
import { mapLine, matchupPageMeta, matchupStaticParams, pairCells, pairSummary, rivalsOf, teamOf } from "@/lib/matchupView";
import { civLabel, SITE_NAME } from "@/lib/share";

/**
 * Todas las páginas se arman al compilar: las de cada civ y las de cada pareja (con las claves en orden alfabético).
 * La pareja al revés y lo que no es ni civ ni pareja los resuelve src/proxy.ts antes de llegar acá (308 y 404 de
 * verdad: con el prerenderizado parcial, una redirección o un notFound armados al pedirlos salían con 200).
 */
export function generateStaticParams() {
  return matchupStaticParams(getMatchups());
}

function mapChoices(data: MatchupData, locale: Locale): Choice[] {
  return [{ index: 0, label: getDictionary(locale).matchups.allMaps }, ...data.mapKeys.map((key, i) => ({ index: i + 1, label: mapName(key, locale) }))];
}

function eloChoices(data: MatchupData, locale: Locale): Choice[] {
  return [0, 1, 2, 3, 4, 5].map((band) => ({ index: band, label: band === 0 ? getDictionary(locale).matchups.allElos : eloLabel(data, band, locale) }));
}

export async function generateMetadata({ params }: PageProps<"/[locale]/cruces/[slug]">): Promise<Metadata> {
  const { slug, locale: raw } = await params;
  const locale = asLocale(raw);
  const meta = matchupPageMeta(slug, locale);
  if (!meta) return { title: getDictionary(locale).matchups.notFoundTitle, robots: { index: false } };
  const { title, description, path } = meta;
  return {
    title,
    description,
    alternates: localeAlternates(locale, path),
    openGraph: { siteName: SITE_NAME, locale: OG_LOCALE[locale], type: "article", title, description, url: localePath(locale, path) },
  };
}

export default async function MatchupPage({ params }: PageProps<"/[locale]/cruces/[slug]">) {
  const { slug } = await params;
  const { locale, t } = await i18n();
  const parsed = parseSlug(slug);
  if (!parsed) notFound();
  if (!("civ" in parsed) && !parsed.canonical) permanentRedirect(localePath(locale, `/cruces/${pairSlug(parsed.a, parsed.b)}`));
  const data = getMatchups();
  if (!data) {
    return (
      <div className="flex flex-col gap-4">
        <h1 className="text-2xl font-semibold">{t.matchups.shortTitle}</h1>
        <p className="text-muted">{t.matchups.noData}</p>
      </div>
    );
  }
  return "civ" in parsed ? <CivMatchups data={data} civ={parsed.civ} locale={locale} /> : <PairMatchup data={data} a={parsed.a} b={parsed.b} locale={locale} />;
}

function Breadcrumb({ civ, locale }: { civ?: string; locale: Locale }) {
  const t = getDictionary(locale).matchups;
  return (
    <nav aria-label={t.breadcrumb} className="text-sm">
      <Link href="/cruces" className="text-faint hover:text-amber">
        {t.crumb}
      </Link>
      {civ ? (
        <>
          <span className="text-faint"> · </span>
          <Link href={`/cruces/${civ}`} className="text-faint hover:text-amber">
            {civLabel(civ, locale)}
          </Link>
        </>
      ) : null}
    </nav>
  );
}

/** Una civ contra todas en 1v1 (con mapa y Elo para elegir) y cómo le va por equipos en cada mapa. */
function CivMatchups({ data, civ, locale }: { data: MatchupData; civ: string; locale: Locale }) {
  const t = getDictionary(locale).matchups;
  const number = (n: number) => n.toLocaleString(LANG_TAG[locale]);
  const rivals = rivalsOf(data, civ);
  const teamCells = teamOf(data, civ);
  if (rivals.length === 0 && !teamCells) notFound();
  const name = civLabel(civ, locale);
  const names = { civ: name, civLower: name.toLowerCase() };
  const maps = mapChoices(data, locale);
  return (
    <article className="flex flex-col gap-8">
      <header className="flex flex-col gap-2">
        <Breadcrumb locale={locale} />
        <h1 className="text-2xl font-semibold">{fill(t.civHeading, names)}</h1>
        <p className="max-w-3xl text-muted">{fill(t.civIntro, names)}</p>
      </header>

      {rivals.length > 0 ? (
        <section aria-labelledby="rivales" className="flex flex-col gap-3">
          <h2 id="rivales" className="text-lg font-semibold">
            {t.oneVsOne}
          </h2>
          <MatchupRivals
            civName={name}
            rivals={rivals.map((r) => ({ rival: r.rival, name: civLabel(r.rival, locale), slug: pairSlug(civ, r.rival), cells: r.cells }))}
            maps={maps}
            elos={eloChoices(data, locale)}
          />
        </section>
      ) : null}

      {teamCells ? (
        <section aria-labelledby="equipos" className="flex flex-col gap-3">
          <h2 id="equipos" className="text-lg font-semibold">
            {t.teams}
          </h2>
          <p className="max-w-3xl text-sm text-muted">{fill(t.teamsIntro, names)}</p>
          <div className="overflow-x-auto rounded-md border border-line">
            <table className="w-full min-w-[26rem] text-sm">
              <thead className="bg-panel-2 text-left text-faint">
                <tr>
                  <th className="px-3 py-2 font-normal">{t.map}</th>
                  <th className="px-3 py-2 font-normal">{t.wins}</th>
                  <th className="px-3 py-2 text-right font-normal">{t.appearances}</th>
                </tr>
              </thead>
              <tbody>
                {maps.map((m) => {
                  const c = teamCells[m.index * 6];
                  return (
                    <tr key={m.index} className="border-t border-line">
                      <td className="px-3 py-1.5">{m.label}</td>
                      <td className={`px-3 py-1.5 tabular-nums ${chanceColor(c)}`}>
                        {chanceText(c, locale)}
                        {isEven(c) ? <span className="text-faint">{t.evenDot}</span> : null}
                      </td>
                      <td className="px-3 py-1.5 text-right tabular-nums text-muted">{c.n > 0 ? number(c.n) : t.estimatedCell}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </section>
      ) : null}

      <Method data={data} locale={locale} />
    </article>
  );
}

/** El cruce de dos civs: el general desde los dos lados, por mapa y por duración. */
function PairMatchup({ data, a, b, locale }: { data: MatchupData; a: string; b: string; locale: Locale }) {
  if (!oneVsOne(data, a, b, 0, 0, 0)) notFound();
  const t = getDictionary(locale).matchups;
  const number = (n: number) => n.toLocaleString(LANG_TAG[locale]);
  const [nameA, nameB] = [civLabel(a, locale), civLabel(b, locale)];
  const names = { a: nameA, b: nameB, aLower: nameA.toLowerCase(), bLower: nameB.toLowerCase() };
  const summary = pairSummary(data, a, b, locale);
  const { general } = summary;
  const estimated = mapLine(general, null, locale);
  return (
    <article className="flex flex-col gap-8">
      <header className="flex flex-col gap-2">
        <Breadcrumb civ={a} locale={locale} />
        <h1 className="text-2xl font-semibold">{fill(t.pairHeading, names)}</h1>
        <p className="text-lg">
          <span className={chanceColor(general)}>
            {fill(t.civWins, { civ: nameA })} {chanceText(general, locale)}
          </span>
          {isEven(general) ? <span className="text-faint">{t.evenParens}</span> : null}
          <span className="text-muted">{fill(t.pairRival, { ...names, percent: summary.rivalPercent })}</span>
        </p>
        <p className="text-sm text-faint">
          {general.n > 0 ? fill(t.pairGames, { n: number(general.n) }) : `${estimated} `}
          <Link href={`/cruces/${b}`} className="hover:text-amber">
            {fill(t.pairRivalLink, names)}
          </Link>
        </p>
      </header>

      <section aria-labelledby="mapas" className="flex flex-col gap-3">
        <h2 id="mapas" className="text-lg font-semibold">
          {t.byMap}
        </h2>
        <div className="overflow-x-auto rounded-md border border-line">
          <table className="w-full min-w-[26rem] text-sm">
            <thead className="bg-panel-2 text-left text-faint">
              <tr>
                <th className="px-3 py-2 font-normal">{t.map}</th>
                <th className="px-3 py-2 font-normal">{fill(t.civWins, { civ: nameA })}</th>
                <th className="px-3 py-2 text-right font-normal">{t.games}</th>
              </tr>
            </thead>
            <tbody>
              {summary.maps.map((m) => (
                <tr key={m.name} className="border-t border-line">
                  <td className="px-3 py-1.5">{m.name}</td>
                  <td className={`px-3 py-1.5 tabular-nums ${chanceColor(m.cell)}`}>
                    {chanceText(m.cell, locale)}
                    {isEven(m.cell) ? <span className="text-faint">{t.evenDot}</span> : null}
                  </td>
                  <td className="px-3 py-1.5 text-right tabular-nums text-muted">
                    {m.cell.n > 0 ? number(m.cell.n) : t.estimatedCell}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <section aria-labelledby="duracion" className="flex flex-col gap-3">
        <h2 id="duracion" className="text-lg font-semibold">
          {t.byDuration}
        </h2>
        <p className="max-w-3xl text-sm text-faint">{t.durationNote}</p>
        <MatchupDuration
          cells={pairCells(data, a, b)}
          civName={nameA}
          rivalName={locale === "es" ? nameB.toLowerCase() : nameB}
          maps={mapChoices(data, locale)}
          elos={eloChoices(data, locale)}
          durations={[1, 2, 3, 4, 5].map((band) => durationLabel(data, band, locale))}
        />
      </section>

      <Method data={data} locale={locale} />
    </article>
  );
}

/** "Cómo se calcula", la fuente, las fechas y el parche. */
function Method({ data, locale }: { data: MatchupData; locale: Locale }) {
  const t = getDictionary(locale).matchups;
  const number = (n: number) => n.toLocaleString(LANG_TAG[locale]);
  return (
    <section aria-labelledby="como" className="flex flex-col gap-2 text-sm text-muted">
      <h2 id="como" className="text-lg font-semibold text-foreground">
        {t.methodTitle}
      </h2>
      <ul className="flex max-w-3xl list-disc flex-col gap-1 pl-5">
        {t.method.map((item) => (
          <li key={item}>{item}</li>
        ))}
      </ul>
      <p className="text-faint">
        {t.source}
        {data.source.url ? (
          <a href={data.source.url} className="hover:text-amber" rel="noopener noreferrer" target="_blank">
            {data.source.name ?? data.source.url}
          </a>
        ) : (
          (data.source.name ?? t.sourceFallback)
        )}
        {data.source.from && data.source.to ? fill(t.sourceDates, { from: data.source.from, to: data.source.to }) : ""}
        {data.patch.label ? fill(t.patch, { label: data.patch.label }) : ""}
        {data.patch.previousMixed ? t.sourcePrevious : ""}
        {fill(t.sourceMatches, { oneVsOne: number(data.matches.oneVsOne), team: number(data.matches.team) })}
      </p>
    </section>
  );
}
