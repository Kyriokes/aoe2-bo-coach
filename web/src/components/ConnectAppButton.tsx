"use client";

import { useRouter } from "next/navigation";
import { useState, useTransition } from "react";
import { createAppToken } from "@/app/actions";

/**
 * Conecta la app de escritorio de un toque: crea un token y abre bocoach://conectar/<token>, que la app guarda. Si la
 * app no se abre (no está instalada o es una versión vieja), el token queda a la vista para pegarlo a mano.
 */
export function ConnectAppButton() {
  const router = useRouter();
  const [token, setToken] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [pending, startTransition] = useTransition();

  function connect() {
    startTransition(async () => {
      const result = await createAppToken("BO Coach");
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
          Conectar la app
        </button>
      </div>
      {token ? (
        <div className="rounded-md border border-amber bg-panel p-3 text-sm">
          <p className="text-amber">Si la app no se abrió, pegá este token en Cuenta de la web. No se vuelve a mostrar.</p>
          <p className="mt-2 break-all font-mono text-xs">{token}</p>
          <button type="button" className="button mt-2" onClick={() => navigator.clipboard.writeText(token)}>
            Copiar
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
