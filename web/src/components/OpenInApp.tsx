"use client";

import { useState } from "react";

/**
 * Llevar la BO a la app: el enlace bocoach://import/<código> la abre directo (si la app registró el enlace), y si
 * no, se puede pegar el código en "Importar desde la web" o descargar el .json.
 */
export function OpenInApp({ code, json, fileName }: { code: string; json: string; fileName: string }) {
  const [copied, setCopied] = useState(false);

  function download() {
    const url = URL.createObjectURL(new Blob([json + "\n"], { type: "application/json" }));
    const link = document.createElement("a");
    link.href = url;
    link.download = `${fileName.replace(/[\/:*?"<>|]/g, "_")}.json`;
    link.click();
    URL.revokeObjectURL(url);
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
    </section>
  );
}
