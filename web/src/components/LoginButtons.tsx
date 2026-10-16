"use client";

import { useState } from "react";
import { browserClient } from "@/lib/supabase/browser";

const PROVIDERS = [
  { id: "discord", label: "Entrar con Discord" },
  { id: "google", label: "Entrar con Google" },
] as const;

export function LoginButtons({ next }: { next: string }) {
  const [error, setError] = useState<string | null>(null);

  async function signIn(provider: (typeof PROVIDERS)[number]["id"]) {
    const redirectTo = `${window.location.origin}/auth/callback?next=${encodeURIComponent(next)}`;
    const { error } = await browserClient().auth.signInWithOAuth({ provider, options: { redirectTo } });
    if (error) setError(error.message);
  }

  return (
    <div className="flex flex-col gap-2">
      {PROVIDERS.map((p) => (
        <button key={p.id} type="button" onClick={() => signIn(p.id)} className="button justify-center py-2">
          {p.label}
        </button>
      ))}
      {error ? <p className="text-sm text-red">{error}</p> : null}
    </div>
  );
}
