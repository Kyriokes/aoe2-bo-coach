"use client";

import { useState } from "react";

/**
 * Llevar la BO a la app: el enlace bocoach://import/<código> la abre directo (si la app registró el enlace), y si
 * no, se puede pegar el código en "Importar desde la web" o descargar el .json.
 */
export function OpenInApp({ code, fileName }: { code: string; fileName: string }) {
  const [copied, setCopied] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // El JSON se pide al descargar (la misma API que usa la app), así la página no lo lleva dos veces.
  async function download() {
    setError(null);
    try {
      const response = await fetch(`/api/bo/${code}`);
      if (!response.ok) throw new Error(`la web respondió ${response.status}`);
      const { build_order } = (await response.json()) as { build_order: unknown };
      const url = URL.createObjectURL(new Blob([JSON.stringify(build_order, null, 2) + "\n"], { type: "application/json" }));
      const link = document.createElement("a");
      link.href = url;
      link.download = `${fileName.replace(/[\/:*?"<>|]/g, "_")}.json`;
      link.click();
      URL.revokeObjectURL(url);
    } catch (e) {
      setError(`No se pudo descargar: ${e instanceof Error ? e.message : String(e)}`);
    }
  }

  async function copy() {
    await navigator.clipboard.writeText(code);
    setCopied(true);
    setTimeout(() => setCopied(false), 1500);
  }

  return (
    <section className="flex flex-wrap items-center gap-3 rounded-lg border border-line bg-panel p-4">
      <a href={`bocoach://import/${code}`} className="button-primary no-underline">
        Abrir en BO Coach
      </a>
      <button type="button" onClick={copy} className="button">
        Código <span className="font-mono text-amber">{code}</span> <span aria-live="polite">{copied ? "· copiado" : "· copiar"}</span>
      </button>
      <button type="button" onClick={download} className="button">
        Descargar .json
      </button>
      <p className="w-full text-xs text-faint">
        Si el botón no abre la app: en BO Coach usá «Importar desde la web» y pegá el código.
      </p>
      {error ? (
        <p role="alert" className="w-full text-xs text-red">
          {error}
        </p>
      ) : null}
    </section>
  );
}
