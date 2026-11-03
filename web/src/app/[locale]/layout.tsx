import type { Metadata, Viewport } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import { Header } from "@/components/Header";
import { LanguageHint } from "@/components/LanguageHint";
import { SupportLinks } from "@/components/SupportSection";
import { fill, getDictionary, rich } from "@/i18n";
import { I18nProvider } from "@/i18n/client";
import Link from "@/i18n/Link";
import { asLocale, LOCALES, OG_LOCALE } from "@/i18n/locales";
import { SITE_NAME } from "@/lib/share";
import { siteUrl } from "@/lib/site";
import "../globals.css";

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

/** Cada idioma es un juego de páginas prerenderizadas. */
export function generateStaticParams() {
  return LOCALES.map((locale) => ({ locale }));
}

export async function generateMetadata({ params }: LayoutProps<"/[locale]">): Promise<Metadata> {
  const locale = asLocale((await params).locale);
  const t = getDictionary(locale).meta;
  const title = fill(t.title, { site: SITE_NAME });
  return {
    metadataBase: new URL(siteUrl()),
    title: { default: title, template: `%s · ${SITE_NAME}` },
    description: t.description,
    applicationName: SITE_NAME,
    openGraph: { siteName: SITE_NAME, locale: OG_LOCALE[locale], type: "website", title, description: t.description },
    twitter: { card: "summary_large_image", title, description: t.description },
    category: "games",
    // Google Search Console: el código de la etiqueta HTML (es público: solo prueba que la web es nuestra). Una variable
    // de entorno lo cambia sin tocar el código.
    verification: { google: process.env.GOOGLE_SITE_VERIFICATION || "ttNIjtGlD8HzEqSy6SUGTQcYGl4m-4nOSF-XiqsP8qM" },
  };
}

export const viewport: Viewport = { themeColor: "#1e1a16", colorScheme: "dark" };

export default async function RootLayout({ children, params }: LayoutProps<"/[locale]">) {
  const locale = asLocale((await params).locale);
  const t = getDictionary(locale).layout;
  return (
    <html lang={locale} className={`${geistSans.variable} ${geistMono.variable} h-full antialiased`}>
      <body className="flex min-h-full flex-col font-sans">
        <I18nProvider locale={locale}>
          <a href="#contenido" className="skip-link">
            {t.skipToContent}
          </a>
          <Header />
          <LanguageHint />
          <main id="contenido" tabIndex={-1} className="mx-auto w-full max-w-5xl flex-1 px-4 py-6 focus:outline-none">
            {children}
          </main>
          <footer className="border-t border-line px-4 py-4 text-center text-xs text-faint">
            {t.footerAbout}{" "}
            <a
              href="https://portfoliosergiofb.vercel.app/es"
              target="_blank"
              rel="noopener"
              className="hover:text-amber"
            >
              Sergio Ferrari Bryce
            </a>
            .{" "}
            <Link href="/roadmap" className="hover:text-amber">
              {t.footerRoadmap}
            </Link>
            {" · "}
            <Link href="/privacidad" className="hover:text-amber">
              {t.footerPrivacy}
            </Link>
            <SupportLinks />
            {/* El aviso que piden las reglas de Microsoft para usar contenido del juego (los íconos del editor). */}
            <p className="mx-auto mt-2 max-w-3xl">
              {rich(t.footerLegal, {
                rules: (text) => (
                  <a href="https://www.xbox.com/en-US/developers/rules" target="_blank" rel="noopener noreferrer" className="hover:text-amber">
                    {text}
                  </a>
                ),
              })}
            </p>
          </footer>
        </I18nProvider>
      </body>
    </html>
  );
}
