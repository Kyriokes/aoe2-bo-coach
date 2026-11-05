"use client";

import { useState } from "react";
import { fill } from "@/i18n";
import { useI18n } from "@/i18n/client";
import { copyText } from "@/lib/clipboard";

/**
 * Llevar la BO a la app: el enlace bocoach://import/<código> la abre directo (si la app registró el enlace), y si
 * no, se puede pegar el código en "Importar desde la web" o descargar el .json.
 */
export function OpenInApp({ code, fileName }: { code: string; fileName: string }) {
  const { t: dict } = useI18n();
  const t = dict.actions;
  const [copied, setCopied] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // El JSON se pide al descargar (la misma API que usa la app), así la página no lo lleva dos veces.
  async function download() {
    setError(null);
    try {
      const response = await fetch(`/api/bo/${code}`);
      if (!response.ok) throw new Error(fill(t.serverSaid, { status: response.status }));
      const { build_order } = (await response.json()) as { build_order: unknown };
      const url = URL.createObjectURL(new Blob([JSON.stringify(build_order, null, 2) + "\n"], { type: "application/json" }));
      const link = document.createElement("a");
      link.href = url;
      link.download = `${fileName.replace(/[\/:*?"<>|]/g, "_")}.json`;
      link.click();
      URL.revokeObjectURL(url);
    } catch (e) {
      setError(fill(t.downloadFailed, { error: e instanceof Error ? e.message : String(e) }));
    }
  }

  async function copy() {
    setError(null);
    if (!(await copyText(code))) {
      setError(t.copyFailed);
      return;
    }
    setCopied(true);
    setTimeout(() => setCopied(false), 1500);
  }

  return (
    <section className="flex flex-wrap items-center gap-3 rounded-lg border border-line bg-panel p-4">
      <a href={`bocoach://import/${code}`} className="button-primary no-underline">
        {t.openInApp}
      </a>
      <button type="button" onClick={copy} className="button">
        {t.code} <span className="font-mono text-amber">{code}</span> <span aria-live="polite">{copied ? t.copied : t.copyAction}</span>
      </button>
      <button type="button" onClick={download} className="button">
        {t.downloadJson}
      </button>
      <p className="w-full text-xs text-faint">
        {t.openInAppHelp}
      </p>
      {error ? (
        <p role="alert" className="w-full text-xs text-red">
          {error}
        </p>
      ) : null}
    </section>
  );
}
