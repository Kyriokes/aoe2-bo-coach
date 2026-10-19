"use client";

import { useEffect, useState } from "react";
import { COACH_BASE } from "@/lib/coach/browser";
import { BuildOrderView } from "./BuildOrderView";

interface Example {
  file: string;
  name: string;
}

/** Las BO de ejemplo de la app, simuladas en el navegador con el mismo Core (no necesita la base de datos). */
export function ExampleDemo() {
  const [examples, setExamples] = useState<Example[]>([]);
  const [selected, setSelected] = useState<string>("");
  const [json, setJson] = useState<string | null>(null);

  useEffect(() => {
    fetch(`${COACH_BASE}/ejemplos/index.json`)
      .then((r) => (r.ok ? (r.json() as Promise<Example[]>) : []))
      .then((list) => {
        setExamples(list);
        // Primero la de scouts: es la más corta de leer.
        const first = list.find((e) => e.name.toLowerCase().includes("scouts")) ?? list[0];
        if (first) setSelected(first.file);
      })
      .catch(() => setExamples([]));
  }, []);

  useEffect(() => {
    if (!selected) return;
    let cancelled = false;
    fetch(`${COACH_BASE}/ejemplos/${selected}`)
      .then((r) => r.text())
      .then((text) => {
        if (!cancelled) setJson(text);
      })
      .catch(() => {
        if (!cancelled) setJson(null);
      });
    return () => {
      cancelled = true;
    };
  }, [selected]);

  if (examples.length === 0) return <p className="text-sm text-faint">Cargando los ejemplos…</p>;

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap gap-2" role="group" aria-label="Elegí una build order de ejemplo">
        {examples.map((ex) => (
          <button
            key={ex.file}
            type="button"
            onClick={() => setSelected(ex.file)}
            aria-pressed={ex.file === selected}
            className={ex.file === selected ? "button-primary" : "button"}
          >
            {ex.name}
          </button>
        ))}
      </div>
      {json ? <BuildOrderView key={selected} json={json} /> : <p className="text-sm text-faint">Cargando…</p>}
    </div>
  );
}
