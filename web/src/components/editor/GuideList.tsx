"use client";

import { useState, type ReactNode } from "react";
import { formatClock } from "@/lib/buildOrder";
import type { Analysis } from "@/lib/coach/types";
import type { EditorStep } from "@/lib/editor/model";
import { chainLabel } from "@/lib/editor/villagers";

interface Chain {
  steps: EditorStep[];
  /** El paso que se elige al tocar la fila (en "aldeano 7: campamento", el campamento). */
  mainId: string;
}

function chainsOf(steps: EditorStep[], newVillagerBuilds: Set<string>): Chain[] {
  const chains: Chain[] = [];
  for (const step of steps) {
    if (step.withPrevious && chains.length > 0) chains[chains.length - 1].steps.push(step);
    else chains.push({ steps: [step], mainId: step.id });
  }
  for (const chain of chains) {
    const built = chain.steps.find((s) => newVillagerBuilds.has(s.id));
    if (built) chain.mainId = built.id;
  }
  return chains;
}

/**
 * Los pasos en orden, escritos como en las guías ("Aldeanos 4 a 6 → ovejas", "Aldeano 7: campamento maderero →
 * madera"), una fila por paso o paso mixto, con sus tiempos simulados. En el celular es el editor principal: la fila
 * elegida se abre con la cantidad, antes, después, borrar, agregar abajo y el resto de las opciones (`panel`).
 */
export function GuideList({
  steps,
  analysis,
  labels,
  newVillagerBuilds,
  selected,
  expanded,
  panel,
  textOf,
  countOf,
  onSelect,
  onCount,
  onMove,
  onRemove,
  onAddBelow,
}: {
  steps: EditorStep[];
  analysis: Analysis | null;
  /** Los textos como en las guías (`guideLabels`). */
  labels: Map<string, string>;
  newVillagerBuilds: Set<string>;
  selected: string | null;
  /** La fila elegida se abre con sus controles (celular). */
  expanded: boolean;
  /** Las opciones del paso elegido (el panel), dentro de la fila abierta. */
  panel: ReactNode;
  textOf: (step: EditorStep) => string;
  /** El paso cuya cantidad cambian − y + en la fila, si tiene. */
  countOf: (chain: EditorStep[]) => EditorStep | null;
  onSelect: (id: string) => void;
  onCount: (id: string, count: number) => void;
  onMove: (id: string, direction: -1 | 1) => void;
  onRemove: (id: string) => void;
  onAddBelow: () => void;
}) {
  const [more, setMore] = useState(false);
  if (steps.length === 0) return null;
  const times = new Map(analysis?.steps.map((s) => [s.id, s]) ?? []);
  const chains = chainsOf(steps, newVillagerBuilds);

  return (
    <ol className="flex flex-col divide-y divide-line rounded-md border border-line bg-panel" aria-label="Pasos en orden">
      {chains.map((chain, i) => {
        const isSelected = chain.steps.some((s) => s.id === selected);
        // "Aldeano 7: campamento" arranca cuando arranca el campamento (no cuando empieza a entrenarse su aldeano).
        const mainStart = times.get(chain.mainId)?.start;
        const starts = mainStart != null ? [mainStart] : chain.steps.map((s) => times.get(s.id)?.start).filter((t): t is number => t != null);
        const ends = chain.steps.map((s) => times.get(s.id)?.end).filter((t): t is number => t != null);
        const neverStarts = analysis?.valid === true && chain.steps.some((s) => times.get(s.id)?.start === null);
        const notes = chain.steps.map((s) => s.note).filter(Boolean);
        const counted = countOf(chain.steps);
        return (
          <li key={chain.steps[0].id} className={isSelected ? "bg-panel-2" : ""}>
            <button
              type="button"
              onClick={() => {
                setMore(false);
                onSelect(chain.mainId);
              }}
              aria-expanded={expanded ? isSelected : undefined}
              className="flex w-full items-baseline gap-3 px-3 py-2 text-left text-sm hover:bg-panel-2"
            >
              <span className="w-6 shrink-0 text-right text-faint">{i + 1}</span>
              <span className="min-w-0 flex-1">
                {chainLabel(chain.steps, steps, labels, textOf)}
                {notes.length > 0 ? <span className="text-xs text-muted"> · {notes.join(" · ")}</span> : null}
              </span>
              <span className={`shrink-0 font-mono text-xs ${neverStarts ? "text-red" : "text-faint"}`}>
                {neverStarts ? "no arranca" : starts.length > 0 ? formatClock(Math.min(...starts)) : ""}
                {!neverStarts && ends.length > 0 && !expanded ? ` – ${formatClock(Math.max(...ends))}` : ""}
              </span>
            </button>
            {expanded && isSelected ? (
              <div className="flex flex-col gap-2 px-3 pb-3">
                <div className="flex flex-wrap items-center gap-2">
                  {counted ? (
                    <span className="flex items-center rounded-md border border-line" role="group" aria-label="Cantidad">
                      <button type="button" className="h-10 w-10 text-lg font-bold" aria-label="Uno menos" onClick={() => onCount(counted.id, counted.count - 1)} disabled={counted.count <= 1}>
                        −
                      </button>
                      <span className="w-8 text-center font-semibold">{counted.count}</span>
                      <button type="button" className="h-10 w-10 text-lg font-bold" aria-label="Uno más" onClick={() => onCount(counted.id, counted.count + 1)}>
                        +
                      </button>
                    </span>
                  ) : null}
                  <button type="button" className="button h-10" onClick={() => onMove(chain.mainId, -1)} aria-label="Mover antes">
                    ↑
                  </button>
                  <button type="button" className="button h-10" onClick={() => onMove(chain.mainId, 1)} aria-label="Mover después">
                    ↓
                  </button>
                  <button type="button" className="button h-10" onClick={onAddBelow}>
                    + Abajo
                  </button>
                  <button type="button" className="button ml-auto h-10 hover:border-red hover:text-red" onClick={() => onRemove(chain.mainId)}>
                    Borrar
                  </button>
                </div>
                <button type="button" className="self-start text-xs text-amber" aria-expanded={more} onClick={() => setMore((m) => !m)}>
                  {more ? "Menos opciones" : "Más opciones"}
                </button>
                {more ? panel : null}
              </div>
            ) : null}
          </li>
        );
      })}
    </ol>
  );
}
