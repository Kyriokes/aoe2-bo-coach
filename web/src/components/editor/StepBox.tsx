"use client";

import type { PointerEvent as ReactPointerEvent, KeyboardEvent as ReactKeyboardEvent } from "react";
import { formatClock } from "@/lib/buildOrder";
import { BOX_H, type PlacedItem, type Scale } from "@/lib/editor/layout";
import { blockReason } from "@/lib/editor/text";

export const ACTION_COLORS: Record<string, string> = {
  train: "var(--blue)",
  build: "var(--green)",
  research: "var(--amber)",
  move: "var(--faint)",
};

/**
 * Una caja de la línea de tiempo: un paso (o un paso mixto) en su lugar simulado, con el tramo rayado de la espera
 * antes de arrancar. Se arrastra con el puntero (de costado la mueve; para arriba o abajo cambia la cantidad), se
 * selecciona con un clic, se mueve con Alt+← y Alt+→ y su cantidad cambia con Alt+↑ y Alt+↓.
 */
export function StepBox({
  box,
  top,
  scale,
  selected,
  dragging,
  countEditable,
  onPointerDown,
  onNudge,
  onRemove,
  onSelect,
  onCount,
}: {
  box: PlacedItem;
  top: number;
  scale: Scale;
  selected: boolean;
  dragging: boolean;
  countEditable: boolean;
  onPointerDown: (e: ReactPointerEvent<HTMLDivElement>) => void;
  onNudge: (direction: -1 | 1) => void;
  onRemove: () => void;
  onSelect: () => void;
  onCount: (delta: 1 | -1) => void;
}) {
  const width = Math.max(1, box.x1 - box.x0);
  const leadInWidth = box.leadIn && box.start !== null ? Math.max(0, Math.min(width, scale.x(box.leadIn.to) - box.x0)) : 0;
  const times = box.start !== null ? `de ${formatClock(box.start)} a ${formatClock(box.end ?? box.start)}` : box.neverStarts ? "no arranca" : "sin simular";
  const title = [box.label, times, ...box.blocks.map((b) => `espera de ${formatClock(b.from)} a ${formatClock(b.to)}: ${blockReason(b)}`), countEditable ? "Deslizá para arriba o para abajo para cambiar la cantidad" : null]
    .filter(Boolean)
    .join("\n");

  function onKeyDown(e: ReactKeyboardEvent<HTMLDivElement>) {
    if (e.altKey && (e.key === "ArrowLeft" || e.key === "ArrowRight")) {
      e.preventDefault();
      onNudge(e.key === "ArrowLeft" ? -1 : 1);
    } else if (e.altKey && countEditable && (e.key === "ArrowUp" || e.key === "ArrowDown")) {
      e.preventDefault();
      onCount(e.key === "ArrowUp" ? 1 : -1);
    } else if (e.key === "Delete" || e.key === "Backspace") {
      e.preventDefault();
      onRemove();
    } else if (e.key === "Enter" || e.key === " ") {
      e.preventDefault();
      onSelect();
    }
  }

  return (
    <div
      role="button"
      tabIndex={0}
      aria-label={`${box.label}, ${times}`}
      aria-pressed={selected}
      title={title}
      data-box={box.chainId}
      data-step={box.stepId}
      onPointerDown={onPointerDown}
      onKeyDown={onKeyDown}
      className={`absolute flex select-none items-center overflow-hidden rounded-md text-xs text-background outline-offset-2 transition-[left,width] duration-150 ease-out focus-visible:outline-2 focus-visible:outline-amber ${
        selected ? "ring-2 ring-amber" : ""
      } ${dragging ? "opacity-50" : ""} ${box.neverStarts ? "ring-2 ring-red" : ""}`}
      style={{ left: box.x0, top, width, height: BOX_H, background: ACTION_COLORS[box.kind] ?? "var(--muted)", touchAction: "none", cursor: "grab" }}
    >
      {leadInWidth > 0 ? (
        <span
          aria-hidden="true"
          className="absolute inset-y-0 left-0"
          style={{ width: leadInWidth, background: "repeating-linear-gradient(135deg, rgba(0,0,0,0.35) 0 4px, transparent 4px 8px)" }}
        />
      ) : null}
      {box.what && width - leadInWidth > 40 ? (
        // eslint-disable-next-line @next/next/no-img-element -- íconos del juego, ya chicos (64×64)
        <img src={`/icons/${box.what}.png`} alt="" width={20} height={20} className="relative ml-0.5 shrink-0 rounded-sm" style={{ marginLeft: leadInWidth + 2 }} />
      ) : null}
      <span className="relative truncate px-1.5 font-medium" style={{ marginLeft: box.what && width - leadInWidth > 40 ? 0 : leadInWidth }}>
        {box.label}
      </span>
      {countEditable && selected ? (
        <span className="relative ml-auto flex shrink-0 pr-1">
          <button type="button" aria-label="Uno menos" className="px-1 font-bold hover:bg-black/20" onPointerDown={(e) => e.stopPropagation()} onClick={() => onCount(-1)}>
            −
          </button>
          <button type="button" aria-label="Uno más" className="px-1 font-bold hover:bg-black/20" onPointerDown={(e) => e.stopPropagation()} onClick={() => onCount(1)}>
            +
          </button>
        </span>
      ) : null}
    </div>
  );
}
