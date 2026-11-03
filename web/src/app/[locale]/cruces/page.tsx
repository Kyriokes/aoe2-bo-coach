import type { Metadata } from "next";
import { fill, getDictionary } from "@/i18n";
import Link from "@/i18n/Link";
import { asLocale, LANG_TAG, localeAlternates, localePath, OG_LOCALE } from "@/i18n/locales";
import { i18n } from "@/i18n/server";
import { getMatchups } from "@/lib/matchupsServer";
import { civsWithData } from "@/lib/matchupView";
import { civLabel, SITE_NAME } from "@/lib/share";

export async function generateMetadata({ params }: PageProps<"/[locale]/cruces">): Promise<Metadata> {
  const locale = asLocale((await params).locale);
  const t = getDictionary(locale).matchups;
  return {
    title: t.title,
    description: t.description,
    alternates: localeAlternates(locale, "/cruces"),
    openGraph: { siteName: SITE_NAME, locale: OG_LOCALE[locale], type: "website", title: t.title, description: t.description, url: localePath(locale, "/cruces") },
  };
}

/** Elegir dos civs (el formulario va a /cruces/ir, que redirige al cruce) o abrir una civ. */
export default async function MatchupsPage() {
  const { locale, t: dict } = await i18n();
  const t = dict.matchups;
  const data = getMatchups();
  if (!data) {
    return (
      <div className="flex flex-col gap-4">
        <h1 className="text-2xl font-semibold">{t.title}</h1>
        <p className="text-muted">{t.noData}</p>
      </div>
    );
  }
  const number = (n: number) => n.toLocaleString(LANG_TAG[locale]);
  const civs = civsWithData(data).sort((x, y) => civLabel(x, locale).localeCompare(civLabel(y, locale), locale));
  const options = civs.map((civ) => (
    <option key={civ} value={civ}>
      {civLabel(civ, locale)}
    </option>
  ));
  return (
    <div className="flex flex-col gap-8">
      <header className="flex flex-col gap-2">
        <h1 className="text-2xl font-semibold">{t.title}</h1>
        <p className="max-w-3xl text-muted">{t.intro}</p>
        <p className="text-sm text-faint">
          {fill(t.matches, { oneVsOne: number(data.matches.oneVsOne), team: number(data.matches.team) })}
          {data.source.from && data.source.to ? fill(t.dates, { from: data.source.from, to: data.source.to }) : ""}
          {data.patch.label ? fill(t.patch, { label: data.patch.label }) : ""}.
        </p>
      </header>

      <form action={localePath(locale, "/cruces/ir")} method="get" className="flex flex-wrap items-end gap-3 rounded-md border border-line bg-panel p-4">
        <label className="flex flex-col gap-1 text-sm">
          <span className="text-faint">{t.yourCiv}</span>
          <select name="a" className="field" defaultValue="franks" required>
            {options}
          </select>
        </label>
        <label className="flex flex-col gap-1 text-sm">
          <span className="text-faint">{t.against}</span>
          <select name="b" className="field" defaultValue="britons" required>
            {options}
          </select>
        </label>
        <button type="submit" className="button">
          {t.seePair}
        </button>
      </form>

      <section aria-labelledby="civs" className="flex flex-col gap-3">
        <h2 id="civs" className="text-lg font-semibold">
          {t.eachCiv}
        </h2>
        <ul className="grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-5">
          {civs.map((civ) => (
            <li key={civ}>
              <Link href={`/cruces/${civ}`} className="block rounded border border-line bg-panel px-3 py-2 text-sm hover:text-amber">
                {civLabel(civ, locale)}
              </Link>
            </li>
          ))}
        </ul>
      </section>
    </div>
  );
}
