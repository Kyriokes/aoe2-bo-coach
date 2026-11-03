import type { Metadata } from "next";
import { Suspense } from "react";
import { LoginButtons } from "@/components/LoginButtons";
import { getDictionary } from "@/i18n";
import { asLocale, localePath, type Locale } from "@/i18n/locales";
import { i18n } from "@/i18n/server";

export async function generateMetadata({ params }: PageProps<"/[locale]/login">): Promise<Metadata> {
  const t = getDictionary(asLocale((await params).locale)).login;
  return { title: t.title, robots: { index: false, follow: false } };
}

export default async function LoginPage({ searchParams }: PageProps<"/[locale]/login">) {
  const { locale, t } = await i18n();
  return (
    <div className="mx-auto flex max-w-sm flex-col gap-4 py-8">
      <h1 className="text-2xl font-semibold">{t.login.title}</h1>
      <p className="text-sm text-muted">{t.login.intro}</p>
      <Suspense fallback={null}>
        <Buttons searchParams={searchParams} locale={locale} />
      </Suspense>
    </div>
  );
}

/** Solo rutas internas: evita que el login redirija a otro sitio. */
function safeNext(value: string | string[] | undefined, locale: Locale) {
  return typeof value === "string" && value.startsWith("/") && !value.startsWith("//") ? value : localePath(locale, "/");
}

async function Buttons({ searchParams, locale }: { searchParams: PageProps<"/[locale]/login">["searchParams"]; locale: Locale }) {
  const { volver, error } = await searchParams;
  return (
    <>
      {error ? <p className="text-sm text-red">{getDictionary(locale).login.failed}</p> : null}
      <LoginButtons next={safeNext(volver, locale)} />
    </>
  );
}
