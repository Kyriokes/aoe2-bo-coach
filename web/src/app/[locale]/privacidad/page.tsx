import type { Metadata } from "next";
import { getDictionary, rich } from "@/i18n";
import { asLocale, localeAlternates } from "@/i18n/locales";
import { i18n } from "@/i18n/server";
import { REPO_URL } from "@/lib/site";

export async function generateMetadata({ params }: PageProps<"/[locale]/privacidad">): Promise<Metadata> {
  const locale = asLocale((await params).locale);
  const t = getDictionary(locale).privacy;
  return { title: t.title, description: t.description, alternates: localeAlternates(locale, "/privacidad") };
}

/** Política de privacidad: la pide Google para publicar el inicio de sesión con Google. */
export default async function PrivacyPage() {
  const { t: dict } = await i18n();
  const t = dict.privacy;
  return (
    <article className="flex max-w-2xl flex-col gap-4 text-sm text-muted">
      <h1 className="text-2xl font-semibold text-foreground">{t.title}</h1>
      <p>{t.updated}</p>

      <h2 className="mt-2 text-base font-semibold text-foreground">{t.appTitle}</h2>
      <p>{rich(t.appText, { code: (text) => <code className="font-mono">{text}</code> })}</p>

      <h2 className="mt-2 text-base font-semibold text-foreground">{t.webTitle}</h2>
      <p>{t.webText1}</p>
      <p>{t.webText2}</p>
      <ul className="list-disc pl-5">
        {t.webItems.map((item) => (
          <li key={item}>{item}</li>
        ))}
      </ul>
      <p>{t.webText3}</p>

      <h2 className="mt-2 text-base font-semibold text-foreground">{t.deleteTitle}</h2>
      <p>
        {rich(t.deleteText, {
          em: (text) => <em>{text}</em>,
          github: (text) => (
            <a href={`${REPO_URL}/issues`} className="text-amber hover:underline">
              {text}
            </a>
          ),
        })}
      </p>
    </article>
  );
}
