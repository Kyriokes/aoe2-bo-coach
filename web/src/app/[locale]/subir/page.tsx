import type { Metadata } from "next";
import { Suspense } from "react";
import { BuildOrderEditor } from "@/components/editor/BuildOrderEditor";
import { getDictionary, rich } from "@/i18n";
import Link from "@/i18n/Link";
import { asLocale, localeAlternates, localePath, type Locale } from "@/i18n/locales";
import { i18n } from "@/i18n/server";
import { getOwnBuildOrder } from "@/lib/ownData";
import { getCurrentUser } from "@/lib/session";

export async function generateMetadata({ params }: PageProps<"/[locale]/subir">): Promise<Metadata> {
  const locale = asLocale((await params).locale);
  const t = getDictionary(locale).upload;
  return { title: t.title, description: t.description, alternates: localeAlternates(locale, "/subir") };
}

export default async function UploadPage({ searchParams }: PageProps<"/[locale]/subir">) {
  const { locale, t: dict } = await i18n();
  const t = dict.upload;
  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-2xl font-semibold">{t.title}</h1>
      <p className="max-w-2xl text-sm text-muted">{t.intro}</p>
      <Suspense fallback={<p className="text-sm text-faint">{t.loading}</p>}>
        <Upload searchParams={searchParams} locale={locale} />
      </Suspense>
    </div>
  );
}

async function Upload({ searchParams, locale }: { searchParams: PageProps<"/[locale]/subir">["searchParams"]; locale: Locale }) {
  const t = getDictionary(locale).upload;
  const params = await searchParams;
  const user = await getCurrentUser();
  const editar = typeof params.editar === "string" ? params.editar : undefined;
  // Se lee con la sesión: una privada propia no está en el caché público.
  const ownEdit = editar && user ? await getOwnBuildOrder(editar.toLowerCase()) : null;

  return (
    <>
      {!user ? (
        <p className="rounded-md border border-line bg-panel p-3 text-sm text-muted">
          {rich(t.noAccount, {
            link: (text) => (
              <Link href={`/login?volver=${localePath(locale, "/subir")}`} className="text-amber hover:underline">
                {text}
              </Link>
            ),
          })}
        </p>
      ) : null}
      {ownEdit ? (
        <p className="text-sm text-muted">
          {rich(t.replacing.replace("{name}", ownEdit.name), {
            link: (text) => (
              <Link href={`/bo/${ownEdit.code}`} className="text-amber hover:underline">
                {text}
              </Link>
            ),
          })}
        </p>
      ) : null}
      <BuildOrderEditor
        loggedIn={user !== null}
        editCode={ownEdit?.code}
        initialJson={ownEdit ? JSON.stringify(ownEdit.body) : ""}
        initialVisibility={ownEdit?.visibility}
      />
    </>
  );
}
