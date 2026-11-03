import { rich } from "@/i18n";
import Link from "@/i18n/Link";
import { i18n } from "@/i18n/server";

export default async function NotFound() {
  const { t } = await i18n();
  return (
    <div className="flex max-w-2xl flex-col gap-3">
      <h1 className="text-2xl font-semibold">{t.notFound.title}</h1>
      <p className="text-sm text-muted">{rich(t.notFound.text, { code: (text) => <span className="font-mono text-amber">{text}</span> })}</p>
      <p>
        <Link href="/biblioteca" className="button no-underline">
          {t.notFound.back}
        </Link>
      </p>
    </div>
  );
}
