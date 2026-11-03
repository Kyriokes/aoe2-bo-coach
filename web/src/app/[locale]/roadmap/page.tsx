import type { Metadata } from "next";
import { JsonLd } from "@/components/JsonLd";
import { fill } from "@/i18n";
import Link from "@/i18n/Link";
import { asLocale, LANG_TAG, localeAlternates, localePath, OG_LOCALE } from "@/i18n/locales";
import { i18n } from "@/i18n/server";
import { roadmapContent } from "@/lib/roadmap";
import { SITE_NAME } from "@/lib/share";
import { DOWNLOAD_URL, REPO_URL, siteUrl } from "@/lib/site";

export async function generateMetadata({ params }: PageProps<"/[locale]/roadmap">): Promise<Metadata> {
  const locale = asLocale((await params).locale);
  const { title, description } = roadmapContent(locale);
  return {
    title,
    description,
    alternates: localeAlternates(locale, "/roadmap"),
    openGraph: { siteName: SITE_NAME, locale: OG_LOCALE[locale], type: "article", title, description, url: localePath(locale, "/roadmap") },
  };
}

/**
 * El roadmap para los jugadores. El contenido está en src/content/roadmap.<idioma>.json: se edita ahí, sin tocar este
 * componente (ver lib/roadmap.ts).
 */
export default async function RoadmapPage() {
  const { locale, t } = await i18n();
  const content = roadmapContent(locale);
  const updated = new Date(`${content.updated}T12:00:00`).toLocaleDateString(LANG_TAG[locale], { dateStyle: "long" });
  return (
    <article className="flex max-w-3xl flex-col gap-8">
      <JsonLd
        data={{
          "@context": "https://schema.org",
          "@type": "WebPage",
          name: content.title,
          description: content.description,
          url: `${siteUrl()}${localePath(locale, "/roadmap")}`,
          inLanguage: locale,
          dateModified: content.updated,
        }}
      />
      <header className="flex flex-col gap-3">
        <h1 className="text-3xl font-semibold">{content.title}</h1>
        {content.intro.map((paragraph) => (
          <p key={paragraph} className="text-muted">
            {paragraph}
          </p>
        ))}
        <p className="text-xs text-faint">{fill(t.roadmap.updated, { date: updated })}</p>
      </header>

      {content.sections.map((section) => (
        <section key={section.id} id={section.id} aria-labelledby={`${section.id}-titulo`} className="flex scroll-mt-6 flex-col gap-3">
          <h2 id={`${section.id}-titulo`} className="text-xl font-semibold">
            {section.title}
          </h2>
          {section.intro ? <p className="text-sm text-muted">{section.intro}</p> : null}
          <ul className="flex flex-col gap-2">
            {section.items.map((item) => (
              <li key={item.title} className="rounded-lg border border-line bg-panel p-3">
                <p className="text-sm font-semibold text-foreground">{item.title}</p>
                <p className="mt-1 text-sm text-muted">{item.text}</p>
              </li>
            ))}
          </ul>
        </section>
      ))}

      <footer className="flex flex-col gap-3 rounded-lg border border-amber bg-panel p-4">
        <p className="text-sm text-foreground">{content.closing}</p>
        <div className="flex flex-wrap gap-2">
          <a href={DOWNLOAD_URL} className="button-primary no-underline">
            {t.roadmap.appLink}
          </a>
          <Link href="/biblioteca" className="button no-underline">
            {t.roadmap.libraryLink}
          </Link>
          <a href={`${REPO_URL}/issues`} className="button no-underline">
            {t.roadmap.issuesLink}
          </a>
        </div>
      </footer>
    </article>
  );
}
