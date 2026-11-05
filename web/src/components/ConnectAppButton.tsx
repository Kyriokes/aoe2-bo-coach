"use client";

import { useRouter } from "next/navigation";
import { useState, useTransition } from "react";
import { createAppToken } from "@/app/actions";
import { useI18n } from "@/i18n/client";
import { copyText } from "@/lib/clipboard";
import { safeAction } from "@/lib/safeAction";

/**
 * Conecta la app de escritorio de un toque: crea un token y abre bocoach://conectar/<token>, que la app guarda. Si la
 * app no se abre (no está instalada o es una versión vieja), el token queda a la vista para pegarlo a mano.
 */
export function ConnectAppButton() {
  const { locale, t } = useI18n();
  const router = useRouter();
  const [token, setToken] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [pending, startTransition] = useTransition();

  function connect() {
    startTransition(async () => {
      const result = await safeAction(() => createAppToken("BO Coach", locale, false), t.actions.failed);
      if (!result.ok) {
        setError(result.error);
        return;
      }
      setError(null);
      setToken(result.value);
      window.location.href = `bocoach://conectar/${result.value}`;
      router.refresh();
    });
  }

  return (
    <div className="flex flex-col gap-2">
      <div>
        <button type="button" onClick={connect} disabled={pending} className="button-primary">
          {t.account.connect}
        </button>
      </div>
      {token ? (
        <div className="rounded-md border border-amber bg-panel p-3 text-sm">
          <p className="text-amber">{t.account.connectNotice}</p>
          <p className="mt-2 break-all font-mono text-xs">{token}</p>
          <button type="button" className="button mt-2" onClick={async () => setError((await copyText(token)) ? null : t.actions.copyFailed)}>
            {t.account.copy}
          </button>
        </div>
      ) : null}
      {error ? (
        <p role="alert" className="text-sm text-red">
          {error}
        </p>
      ) : null}
    </div>
  );
}
