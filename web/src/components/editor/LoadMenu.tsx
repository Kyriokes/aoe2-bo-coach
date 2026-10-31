"use client";

import { useEffect, useState } from "react";
import { COACH_BASE } from "@/lib/coach/browser";

interface Example {
  file: string;
  name: string;
}

/**
 * Cómo entra una BO al editor: un ejemplo de los que trae la app, un archivo .json, el JSON pegado, o una nueva de
 * cero. El archivo y lo pegado pueden ser una guía de buildorderguide.com o de RTS Overlay: el editor la convierte con
 * el Core. Todo termina en `onLoad(json)`.
 */
export function LoadMenu({ editCode, onLoad, onNew }: { editCode?: string; onLoad: (json: string) => void; onNew: () => void }) {
  const [examples, setExamples] = useState<Example[]>([]);
  const [pasteOpen, setPasteOpen] = useState(false);
  const [pasted, setPasted] = useState("");

  useEffect(() => {
    fetch(`${COACH_BASE}/ejemplos/index.json`)
      .then((r) => (r.ok ? (r.json() as Promise<Example[]>) : []))
      .then(setExamples)
      .catch(() => setExamples([]));
  }, []);

  async function loadExample(file: string) {
    if (!file) return;
    onLoad(await fetch(`${COACH_BASE}/ejemplos/${file}`).then((r) => r.text()));
  }

  async function readFile(file: File | undefined) {
    if (!file) return;
    onLoad(await file.text());
  }

  return (
    <div className="flex flex-col gap-2">
      <div className="flex flex-wrap items-center gap-2">
        <button type="button" className="button" onClick={onNew}>
          Nueva
        </button>
        {examples.length > 0 && !editCode ? (
          <select className="field" value="" onChange={(e) => loadExample(e.target.value)} aria-label="Cargar un ejemplo">
            <option value="" disabled>
              Cargar un ejemplo…
            </option>
            {examples.map((ex) => (
              <option key={ex.file} value={ex.file}>
                {ex.name}
              </option>
            ))}
          </select>
        ) : null}
        <label className="button cursor-pointer has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-amber">
          Abrir .json
          <input type="file" accept=".json,application/json" className="sr-only" onChange={(e) => readFile(e.target.files?.[0])} />
        </label>
        <button type="button" className="button" aria-expanded={pasteOpen} onClick={() => setPasteOpen((open) => !open)}>
          Pegar JSON o guía
        </button>
      </div>
      {pasteOpen ? (
        <div className="flex flex-col gap-2">
          <textarea
            value={pasted}
            onChange={(e) => setPasted(e.target.value)}
            rows={6}
            spellCheck={false}
            placeholder="El JSON de una BO, o una guía copiada de buildorderguide.com (Copy to clipboard for RTS Overlay)"
            aria-label="JSON de la build order o guía"
            className="field font-mono text-xs"
          />
          <div>
            <button
              type="button"
              className="button"
              disabled={!pasted.trim()}
              onClick={() => {
                onLoad(pasted);
                setPasteOpen(false);
              }}
            >
              Cargar lo pegado
            </button>
          </div>
        </div>
      ) : null}
    </div>
  );
}
