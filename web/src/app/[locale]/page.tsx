import type { Metadata } from "next";
import Image from "next/image";
import { ExampleDemo } from "@/components/ExampleDemo";
import { JsonLd } from "@/components/JsonLd";
import { SupportSection } from "@/components/SupportSection";
import { getDictionary, rich } from "@/i18n";
import Link from "@/i18n/Link";
import { asLocale, localeAlternates, localePath, type Locale } from "@/i18n/locales";
import { i18n } from "@/i18n/server";
import { GAME } from "@/lib/jsonLd";
import { SITE_NAME } from "@/lib/share";
import { DOWNLOAD_URL, REPO_URL, siteUrl } from "@/lib/site";

export async function generateMetadata({ params }: PageProps<"/[locale]">): Promise<Metadata> {
  return { alternates: localeAlternates(asLocale((await params).locale), "/") };
}

/** La app y la web para los buscadores: una aplicación gratis para Windows y un sitio con buscador de BO. */
function structuredData(locale: Locale) {
  const base = siteUrl();
  const home = locale === "es" ? base : `${base}${localePath(locale, "/")}`;
  return {
    "@context": "https://schema.org",
    "@graph": [
      {
        "@type": "SoftwareApplication",
        "@id": `${home}/#app`,
        name: SITE_NAME,
        description: getDictionary(locale).home.jsonLdDescription,
        applicationCategory: "GameApplication",
        operatingSystem: "Windows 10, Windows 11",
        url: home,
        downloadUrl: DOWNLOAD_URL,
        image: `${home}/opengraph-image`,
        screenshot: `${base}/capturas/overlay.png`,
        inLanguage: locale,
        isAccessibleForFree: true,
        license: "https://opensource.org/licenses/MIT",
        codeRepository: REPO_URL,
        offers: { "@type": "Offer", price: "0", priceCurrency: "USD" },
        about: GAME,
      },
      {
        "@type": "WebSite",
        "@id": `${home}/#web`,
        name: SITE_NAME,
        url: home,
        inLanguage: locale,
        potentialAction: {
          "@type": "SearchAction",
          target: { "@type": "EntryPoint", urlTemplate: `${base}${localePath(locale, "/biblioteca")}?q={search_term_string}` },
          "query-input": "required name=search_term_string",
        },
      },
    ],
  };
}

const github = (text: string) => (
  <a href={REPO_URL} className="text-amber hover:underline">
    {text}
  </a>
);

export default async function HomePage() {
  const { locale, t: dict } = await i18n();
  const t = dict.home;
  return (
    <div className="flex flex-col gap-16 py-4">
      <JsonLd data={structuredData(locale)} />
      <section className="grid items-center gap-8 md:grid-cols-[1.1fr_1fr]">
        <div className="flex flex-col gap-4">
          <p className="text-sm font-medium uppercase tracking-wide text-amber">{t.kicker}</p>
          <h1 className="text-3xl font-semibold leading-tight sm:text-4xl">{t.title}</h1>
          <p className="max-w-xl text-muted">{t.intro}</p>
          <div className="flex flex-wrap gap-3">
            <a href={DOWNLOAD_URL} className="button-primary px-4 py-2 text-base no-underline">
              {t.downloadButton}
            </a>
            <Link href="/biblioteca" className="button px-4 py-2 text-base no-underline">
              {t.libraryButton}
            </Link>
          </div>
          <p className="text-xs text-faint">{t.badges}</p>
          {t.appLanguageNote ? <p className="max-w-xl rounded-md border border-line bg-panel-2 p-2 text-xs text-muted">{t.appLanguageNote}</p> : null}
        </div>
        <figure className="flex flex-col items-center gap-2">
          <Image
            src="/capturas/overlay.png"
            alt={t.overlayAlt}
            width={380}
            height={184}
            preload
            className="w-full max-w-md rounded-lg border border-line shadow-lg"
          />
          <figcaption className="text-xs text-faint">{t.overlayCaption}</figcaption>
        </figure>
      </section>

      <section aria-labelledby="como-funciona" className="flex flex-col gap-4">
        <h2 id="como-funciona" className="text-xl font-semibold">
          {t.howTitle}
        </h2>
        <ol className="grid gap-3 md:grid-cols-3">
          {t.steps.map((step, i) => (
            <li key={step.title} className="rounded-lg border border-line bg-panel p-4">
              <p className="text-sm font-semibold text-amber">
                {i + 1}. {step.title}
              </p>
              <p className="mt-2 text-sm text-muted">{step.text}</p>
            </li>
          ))}
        </ol>
        <p className="rounded-lg border border-line bg-panel-2 p-4 text-sm text-muted">
          {rich(t.noCheat, { strong: (text) => <strong className="text-foreground">{text}</strong> })}
        </p>
      </section>

      <section aria-labelledby="probalo" className="flex flex-col gap-4">
        <div>
          <h2 id="probalo" className="text-xl font-semibold">
            {t.tryTitle}
          </h2>
          <p className="mt-1 max-w-2xl text-sm text-muted">{t.tryText}</p>
        </div>
        <ExampleDemo />
      </section>

      <section aria-labelledby="funciones" className="flex flex-col gap-4">
        <h2 id="funciones" className="text-xl font-semibold">
          {t.featuresTitle}
        </h2>
        <ul className="grid gap-2 sm:grid-cols-2">
          {t.features.map((feature) => (
            <li key={feature} className="rounded-lg border border-line bg-panel p-3 text-sm text-muted">
              {feature}
            </li>
          ))}
        </ul>
      </section>

      <section aria-labelledby="que-viene" className="flex flex-col items-start gap-3 rounded-lg border border-amber bg-panel p-4">
        <h2 id="que-viene" className="text-xl font-semibold">
          {t.roadmapTitle}
        </h2>
        <p className="max-w-2xl text-sm text-muted">{t.roadmapText}</p>
        <Link href="/roadmap" className="button no-underline">
          {t.roadmapButton}
        </Link>
      </section>

      <section id="descargar" aria-labelledby="descargar-titulo" className="flex scroll-mt-6 flex-col gap-4">
        <h2 id="descargar-titulo" className="text-xl font-semibold">
          {t.downloadTitle}
        </h2>
        <ol className="flex max-w-2xl list-decimal flex-col gap-2 pl-5 text-sm text-muted">
          {t.downloadSteps.map((step) => (
            <li key={step}>
              {rich(step, {
                github: (text) => (
                  <a href={DOWNLOAD_URL} className="text-amber hover:underline">
                    {text}
                  </a>
                ),
                code: (text) => <code className="font-mono text-foreground">{text}</code>,
                em: (text) => <em>{text}</em>,
              })}
            </li>
          ))}
        </ol>
        <div>
          <a href={DOWNLOAD_URL} className="button-primary px-4 py-2 text-base no-underline">
            {t.downloadButton}
          </a>
        </div>
      </section>

      <section id="apoyar" aria-labelledby="apoyar-titulo" className="flex scroll-mt-6 flex-col gap-4">
        <h2 id="apoyar-titulo" className="text-xl font-semibold">
          {t.supportTitle}
        </h2>
        <p className="max-w-2xl text-sm text-muted">{t.supportText}</p>
        <SupportSection />
      </section>

      <section aria-labelledby="como-esta-hecho" className="flex flex-col gap-4">
        <h2 id="como-esta-hecho" className="text-xl font-semibold">
          {t.stackTitle}
        </h2>
        <dl className="grid gap-3 sm:grid-cols-2">
          {t.stack.map(({ title, text }) => (
            <div key={title} className="rounded-lg border border-line bg-panel p-4">
              <dt className="text-sm font-semibold text-foreground">{title}</dt>
              <dd className="mt-1 text-sm text-muted">{text}</dd>
            </div>
          ))}
        </dl>
        <p className="text-sm text-muted">{rich(t.codeAt, { github })}</p>
      </section>
    </div>
  );
}
