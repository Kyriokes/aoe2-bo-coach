"use client";

import { useState } from "react";
import { useI18n } from "@/i18n/client";
import { leaveAndComeBack } from "@/lib/editor/draft";
import { browserClient } from "@/lib/supabase/browser";

const PROVIDERS = ["discord", "google"] as const;

export function LoginButtons({ next }: { next: string }) {
  const { t } = useI18n();
  const [error, setError] = useState<string | null>(null);

  async function signIn(provider: (typeof PROVIDERS)[number]) {
    const redirectTo = `${window.location.origin}/auth/callback?next=${encodeURIComponent(next)}`;
    // Se sale del sitio y se vuelve a esta pestaña: el borrador del editor sobrevive, el navegador no tiene que preguntar.
    leaveAndComeBack();
    const { error } = await browserClient().auth.signInWithOAuth({ provider, options: { redirectTo } });
    if (error) {
      leaveAndComeBack(false);
      setError(error.message);
    }
  }

  return (
    <div className="flex flex-col gap-2">
      {PROVIDERS.map((provider) => (
        <button key={provider} type="button" onClick={() => signIn(provider)} className="button justify-center py-2">
          {t.login[provider]}
        </button>
      ))}
      {error ? (
        <p role="alert" className="text-sm text-red">
          {error}
        </p>
      ) : null}
    </div>
  );
}
