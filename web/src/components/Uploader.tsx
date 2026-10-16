"use client";

import { useRouter } from "next/navigation";
import { useCallback, useEffect, useState, useTransition } from "react";
import { publishBuildOrder, updateBuildOrder } from "@/app/actions";
import { COACH_BASE } from "@/lib/coach/browser";
import type { AnalysisResult } from "@/lib/coach/types";
import { BuildOrderView } from "./BuildOrderView";

interface Example {
  file: string;
  name: string;
}

export function Uploader({ loggedIn, editCode, initialJson }: { loggedIn: boolean; editCode?: string; initialJson: string }) {
  const router = useRouter();
  const [text, setText] = useState(initialJson);
  const [reviewed, setReviewed] = useState(initialJson);
  const [analysis, setAnalysis] = useState<AnalysisResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [pending, startTransition] = useTransition();
  const [examples, setExamples] = useState<Example[]>([]);
  const onAnalysis = useCallback((result: AnalysisResult) => setAnalysis(result), []);

  useEffect(() => {
    fetch(`${COACH_BASE}/ejemplos/index.json`)
      .then((r) => (r.ok ? (r.json() as Promise<Example[]>) : []))
      .then(setExamples)
      .catch(() => setExamples([]));
  }, []);

  async function loadExample(file: string) {
    if (!file) return;
    const content = await fetch(`${COACH_BASE}/ejemplos/${file}`).then((r) => r.text());
    setText(content);
    review(content);
  }

  async function readFile(file: File | undefined) {
    if (!file) return;
    const content = await file.text();
    setText(content);
    review(content);
  }

  function review(json: string) {
    setError(null);
    setAnalysis(null);
    setReviewed(json.trim());
  }

  function publish() {
    if (!analysis?.ok) return;
    // Se guarda la BO normalizada por el Core (migrada a la versión actual del formato).
    const json = JSON.stringify(analysis.build_order);
    const feudal = analysis.feudal_click ?? null;
    const castle = analysis.castle_click ?? null;
    startTransition(async () => {
      const result = editCode ? await updateBuildOrder(editCode, json, feudal, castle) : await publishBuildOrder(json, feudal, castle);
      if (!result.ok) {
        setError(result.error);
        return;
      }
      router.push(`/bo/${editCode ?? (result.value as string)}`);
    });
  }

  const canPublish = loggedIn && analysis?.ok === true && analysis.valid && !pending;

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-col gap-2">
        <label className="text-sm text-muted" htmlFor="bo-json">
          JSON de la build order
        </label>
        <textarea
          id="bo-json"
          value={text}
          onChange={(e) => setText(e.target.value)}
          rows={10}
          spellCheck={false}
          placeholder='{ "schema_version": 1, "name": "…", "steps": [ … ] }'
          className="field font-mono text-xs"
        />
        <div className="flex flex-wrap items-center gap-2">
          <button type="button" className="button" onClick={() => review(text)} disabled={!text.trim()}>
            Revisar y simular
          </button>
          <label className="button cursor-pointer">
            Elegir archivo .json
            <input type="file" accept=".json,application/json" className="hidden" onChange={(e) => readFile(e.target.files?.[0])} />
          </label>
          {examples.length > 0 && !editCode ? (
            <select className="field" defaultValue="" onChange={(e) => loadExample(e.target.value)} aria-label="Probar con un ejemplo">
              <option value="" disabled>
                Probar con un ejemplo…
              </option>
              {examples.map((ex) => (
                <option key={ex.file} value={ex.file}>
                  {ex.name}
                </option>
              ))}
            </select>
          ) : null}
          <button type="button" className="button-primary ml-auto" onClick={publish} disabled={!canPublish}>
            {pending ? "Publicando…" : editCode ? "Guardar la nueva versión" : "Publicar en la biblioteca"}
          </button>
        </div>
        {analysis?.ok && !analysis.valid ? <p className="text-sm text-red">Corregí los errores antes de publicar.</p> : null}
        {error ? <p className="text-sm text-red">{error}</p> : null}
      </div>
      {reviewed ? <BuildOrderView json={reviewed} onAnalysis={onAnalysis} /> : null}
    </div>
  );
}
