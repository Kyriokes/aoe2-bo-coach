"use client";

import { useEffect, useState } from "react";
import { fill } from "@/i18n";
import { useI18n } from "@/i18n/client";
import { loadExample, loadExamples, type Example } from "@/lib/coach/examples";

/**
 * Cómo entra una BO al editor: un ejemplo de los que trae la app, un archivo .json, el JSON pegado, o una nueva de
 * cero. El archivo y lo pegado pueden ser una guía de buildorderguide.com o de RTS Overlay: el editor la convierte con
 * el Core. Todo termina en `onLoad(json)`, que dice si la cargó (puede no entenderla, o el usuario no querer perder lo que hay).
 * Lo que no se pudo ni leer se avisa con `onError`.
 */
export function LoadMenu({ editCode, onLoad, onNew, onError }: { editCode?: string; onLoad: (json: string) => boolean; onNew: () => void; onError: (message: string) => void }) {
  const { t: dict } = useI18n();
  const t = dict.editor.load;
  const [examples, setExamples] = useState<Example[]>([]);
  const [pasteOpen, setPasteOpen] = useState(false);
  const [pasted, setPasted] = useState("");

  useEffect(() => {
    loadExamples()
      .then(setExamples)
      .catch(() => setExamples([]));
  }, []);

  async function read(text: () => Promise<string>) {
    try {
      onLoad(await text());
    } catch (error) {
      onError(fill(dict.editor.readFailed, { error: error instanceof Error ? error.message : String(error) }));
    }
  }

  return (
    <div className="flex flex-col gap-2">
      <div className="flex flex-wrap items-center gap-2">
        <button type="button" className="button" onClick={onNew}>
          {t.new}
        </button>
        {examples.length > 0 && !editCode ? (
          <select className="field" value="" onChange={(e) => read(() => loadExample(e.target.value))} aria-label={t.exampleLabel}>
            <option value="" disabled>
              {t.examplePlaceholder}
            </option>
            {examples.map((ex) => (
              <option key={ex.file} value={ex.file}>
                {ex.name}
              </option>
            ))}
          </select>
        ) : null}
        <label className="button cursor-pointer has-[:disabled]:cursor-default has-[:disabled]:opacity-50 has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-amber">
          {t.openJson}
          <input
            type="file"
            accept=".json,application/json"
            className="sr-only"
            onChange={(e) => {
              const file = e.target.files?.[0];
              // Sin esto, elegir de nuevo el mismo archivo no avisa nada.
              e.target.value = "";
              if (file) read(() => file.text());
            }}
          />
        </label>
        <button type="button" className="button" aria-expanded={pasteOpen} onClick={() => setPasteOpen((open) => !open)}>
          {t.paste}
        </button>
      </div>
      {pasteOpen ? (
        <div className="flex flex-col gap-2">
          <textarea
            value={pasted}
            onChange={(e) => setPasted(e.target.value)}
            rows={6}
            spellCheck={false}
            placeholder={t.pastePlaceholder}
            aria-label={t.pasteLabel}
            className="field font-mono text-xs"
          />
          <div>
            <button
              type="button"
              className="button"
              disabled={!pasted.trim()}
              onClick={() => {
                if (onLoad(pasted)) setPasteOpen(false);
              }}
            >
              {t.pasteLoad}
            </button>
          </div>
        </div>
      ) : null}
    </div>
  );
}
