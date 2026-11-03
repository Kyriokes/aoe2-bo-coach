"use client"; // los límites de error son componentes de cliente

import { fill } from "@/i18n";
import { useI18n } from "@/i18n/client";
import Link from "@/i18n/Link";

/** Si algo falla al armar una página (por ejemplo, la base no responde): un mensaje y la opción de reintentar. */
export default function Error({ error, retry }: { error: unknown; retry: () => void }) {
  const { t: dict } = useI18n();
  const t = dict.error;
  const digest = typeof error === "object" && error !== null && "digest" in error ? String(error.digest) : null;
  return (
    <div role="alert" className="flex max-w-2xl flex-col gap-3">
      <h1 className="text-2xl font-semibold">{t.title}</h1>
      <p className="text-sm text-muted">{t.text}</p>
      {digest ? <p className="font-mono text-xs text-faint">{fill(t.code, { digest })}</p> : null}
      <div className="flex gap-2">
        <button type="button" onClick={() => retry()} className="button-primary">
          {t.retry}
        </button>
        <Link href="/" className="button no-underline">
          {t.home}
        </Link>
      </div>
    </div>
  );
}
