"use client";

import { useEffect, useState } from "react";
import { useI18n } from "@/i18n/client";
import { loadExample, loadExamples, pickExample, type Example } from "@/lib/coach/examples";
import { BuildOrderView } from "./BuildOrderView";

/** Las BO de ejemplo de la app, simuladas en el navegador con el mismo Core (no necesita la base de datos). */
export function ExampleDemo() {
  const { t } = useI18n();
  const [examples, setExamples] = useState<Example[]>([]);
  const [listFailed, setListFailed] = useState(false);
  const [selected, setSelected] = useState<string>("");
  /** El último ejemplo que respondió: `json` null si no se pudo traer. */
  const [loaded, setLoaded] = useState<{ file: string; json: string | null } | null>(null);

  // Next recrea los efectos cada vez que la portada vuelve a mostrarse (Activity): el ejemplo elegido sigue elegido.
  useEffect(() => {
    loadExamples()
      .then((list) => {
        setExamples(list);
        setListFailed(list.length === 0);
        setSelected((current) => pickExample(list, current));
      })
      .catch(() => setListFailed(true));
  }, []);

  useEffect(() => {
    if (!selected) return;
    let cancelled = false;
    loadExample(selected)
      .then((text) => {
        if (!cancelled) setLoaded({ file: selected, json: text });
      })
      .catch(() => {
        if (!cancelled) setLoaded({ file: selected, json: null });
      });
    return () => {
      cancelled = true;
    };
  }, [selected]);

  if (examples.length === 0) return <p className={`text-sm ${listFailed ? "text-red" : "text-faint"}`}>{listFailed ? t.sim.examplesFailed : t.sim.examplesLoading}</p>;
  const current = loaded?.file === selected ? loaded : null;

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap gap-2" role="group" aria-label={t.sim.examplesLabel}>
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
      {current?.json ? (
        <BuildOrderView key={selected} json={current.json} />
      ) : current ? (
        <p role="alert" className="text-sm text-red">
          {t.sim.examplesFailed}
        </p>
      ) : (
        <p className="text-sm text-faint">{t.sim.loadingShort}</p>
      )}
    </div>
  );
}
