"use client";

import { useRouter } from "next/navigation";
import { useState, useTransition } from "react";
import { createAppToken, revokeAppToken } from "@/app/actions";
import { fill } from "@/i18n";
import { useI18n } from "@/i18n/client";
import { LANG_TAG } from "@/i18n/locales";
import { copyText } from "@/lib/clipboard";
import { safeAction } from "@/lib/safeAction";

export interface TokenRow {
  id: string;
  label: string;
  created_at: string;
  last_used_at: string | null;
}

export function TokenManager({ tokens }: { tokens: TokenRow[] }) {
  const { locale, t: dict } = useI18n();
  const t = dict.account;
  const router = useRouter();
  const [label, setLabel] = useState(t.tokenDefaultLabel);
  const [created, setCreated] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [pending, startTransition] = useTransition();

  function create() {
    startTransition(async () => {
      const result = await safeAction(() => createAppToken(label, locale), dict.actions.failed);
      if (!result.ok) {
        setError(result.error);
        return;
      }
      setError(null);
      setCreated(result.value);
      router.refresh();
    });
  }

  function revoke(id: string) {
    if (!confirm(t.revokeConfirm)) return;
    startTransition(async () => {
      const result = await safeAction(() => revokeAppToken(id, locale), dict.actions.failed);
      if (!result.ok) setError(result.error);
      router.refresh();
    });
  }

  const date = (iso: string) => new Date(iso).toLocaleDateString(LANG_TAG[locale]);

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-2">
        <input value={label} onChange={(e) => setLabel(e.target.value)} maxLength={60} className="field" aria-label={t.tokenLabel} />
        <button type="button" onClick={create} disabled={pending} className="button-primary">
          {t.createToken}
        </button>
      </div>
      {created ? (
        <div className="rounded-md border border-amber bg-panel p-3 text-sm">
          <p className="text-amber">{t.copyNow}</p>
          <p className="mt-2 break-all font-mono text-xs">{created}</p>
          <button type="button" className="button mt-2" onClick={async () => setError((await copyText(created)) ? null : dict.actions.copyFailed)}>
            {t.copy}
          </button>
        </div>
      ) : null}
      {error ? <p className="text-sm text-red">{error}</p> : null}
      {tokens.length > 0 ? (
        <ul className="divide-y divide-line rounded-md border border-line bg-panel text-sm">
          {tokens.map((row) => (
            <li key={row.id} className="flex items-center gap-3 px-3 py-2">
              <span className="flex-1">{row.label}</span>
              <span className="text-xs text-faint">
                {fill(t.tokenCreated, { date: date(row.created_at) })} · {row.last_used_at ? fill(t.tokenUsed, { date: date(row.last_used_at) }) : t.tokenUnused}
              </span>
              <button type="button" onClick={() => revoke(row.id)} disabled={pending} className="text-xs text-faint hover:text-red">
                {t.delete}
              </button>
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}
