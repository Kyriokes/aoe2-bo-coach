"use client";

import { useRouter } from "next/navigation";
import { useState, useTransition } from "react";
import { createAppToken, revokeAppToken } from "@/app/actions";

export interface TokenRow {
  id: string;
  label: string;
  created_at: string;
  last_used_at: string | null;
}

export function TokenManager({ tokens }: { tokens: TokenRow[] }) {
  const router = useRouter();
  const [label, setLabel] = useState("Mi PC");
  const [created, setCreated] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [pending, startTransition] = useTransition();

  function create() {
    startTransition(async () => {
      const result = await createAppToken(label);
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
    if (!confirm("¿Borrar este token? La app que lo use va a dejar de poder publicar.")) return;
    startTransition(async () => {
      const result = await revokeAppToken(id);
      if (!result.ok) setError(result.error);
      router.refresh();
    });
  }

  const date = (iso: string) => new Date(iso).toLocaleDateString("es-AR");

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-2">
        <input value={label} onChange={(e) => setLabel(e.target.value)} maxLength={60} className="field" aria-label="Nombre del token" />
        <button type="button" onClick={create} disabled={pending} className="button-primary">
          Crear token
        </button>
      </div>
      {created ? (
        <div className="rounded-md border border-amber bg-panel p-3 text-sm">
          <p className="text-amber">Copiá el token ahora: no se vuelve a mostrar.</p>
          <p className="mt-2 break-all font-mono text-xs">{created}</p>
          <button type="button" className="button mt-2" onClick={() => navigator.clipboard.writeText(created)}>
            Copiar
          </button>
        </div>
      ) : null}
      {error ? <p className="text-sm text-red">{error}</p> : null}
      {tokens.length > 0 ? (
        <ul className="divide-y divide-line rounded-md border border-line bg-panel text-sm">
          {tokens.map((t) => (
            <li key={t.id} className="flex items-center gap-3 px-3 py-2">
              <span className="flex-1">{t.label}</span>
              <span className="text-xs text-faint">
                creado {date(t.created_at)} · {t.last_used_at ? `usado ${date(t.last_used_at)}` : "sin usar"}
              </span>
              <button type="button" onClick={() => revoke(t.id)} disabled={pending} className="text-xs text-faint hover:text-red">
                Borrar
              </button>
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}
