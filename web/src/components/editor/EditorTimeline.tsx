"use client";

import { useMemo, type PointerEvent as ReactPointerEvent, type RefObject } from "react";
import type { Analysis } from "@/lib/coach/types";
import { BOX_H, boxesFor, LANE_H, LANES_TOP, ORDINAL_W, packLanes, PAD_L, PAD_R, type PlacedBox, type Scale } from "@/lib/editor/layout";
import type { EditorStep } from "@/lib/editor/model";
import { ResourceChart, type Marker } from "./ResourceChart";
import { StepBox } from "./StepBox";
import type { DragState } from "./useTimelineDrag";

/**
 * La línea de tiempo del editor: el stock de recursos arriba y las cajas de los pasos en carriles, en píxeles. Con la
 * BO inválida (sin tiempos) las cajas van una al lado de la otra (modo ordinal). Durante un arrastre dibuja la vista
 * previa con la escala con que empezó, la caja movida en su lugar simulado, un fantasma bajo el puntero y el cartel.
 */
export function EditorTimeline({
  steps,
  analysis,
  scale: committedScale,
  mode: committedMode,
  drag,
  selected,
  timelineRef,
  onBoxPointerDown,
  onNudge,
  onRemove,
  onSelect,
  onCount,
  isCountEditable,
}: {
  steps: EditorStep[];
  analysis: Analysis | null;
  scale: Scale;
  mode: "time" | "ordinal";
  drag: DragState | null;
  selected: string | null;
  timelineRef: RefObject<HTMLDivElement | null>;
  onBoxPointerDown: (e: ReactPointerEvent<HTMLDivElement>, box: PlacedBox) => void;
  onNudge: (id: string, direction: -1 | 1) => void;
  onRemove: (id: string) => void;
  onSelect: (id: string) => void;
  onCount: (id: string, delta: 1 | -1) => void;
  isCountEditable: (id: string) => boolean;
}) {
  const scale = drag ? drag.scale : committedScale;
  const mode = drag ? drag.mode : committedMode;
  const shownSteps = drag?.previewSteps ?? steps;
  const shownAnalysis = drag ? (drag.preview ?? analysis) : analysis;
  const { boxes, laneCount } = useMemo(() => packLanes(boxesFor(shownSteps, shownAnalysis), scale, mode), [shownSteps, shownAnalysis, scale, mode]);
  const lanesTop = mode === "time" ? LANES_TOP : 24;
  const height = lanesTop + Math.max(1, laneCount) * LANE_H + 24;
  const innerWidth = mode === "ordinal" ? Math.max(scale.width, PAD_L + boxes.length * ORDINAL_W + PAD_R) : scale.width;
  const markers: Marker[] = [
    shownAnalysis?.feudal_click != null ? { t: shownAnalysis.feudal_click, label: "Feudal", dashed: false } : null,
    shownAnalysis?.castle_click != null ? { t: shownAnalysis.castle_click, label: "Castillos", dashed: false } : null,
    shownAnalysis?.targets.feudal_click_time != null ? { t: shownAnalysis.targets.feudal_click_time, label: "objetivo", dashed: true } : null,
  ].filter((m): m is Marker => m !== null);
  const draggedLane = drag ? (boxes.find((b) => b.id === drag.blockId)?.lane ?? 0) : 0;

  return (
    <>
      <div
        ref={timelineRef}
        className={`relative rounded-md border border-line bg-panel ${mode === "ordinal" ? "overflow-x-auto" : "overflow-hidden"}`}
        style={{ height }}
        aria-label="Línea de tiempo de la build order"
      >
        <div className="relative" style={{ width: innerWidth, height }}>
          {mode === "time" ? <ResourceChart timeline={shownAnalysis?.timeline ?? []} scale={scale} height={height} markers={markers} /> : null}
          {steps.length === 0 && !drag ? (
            <p className="absolute inset-x-0 top-1/2 -translate-y-1/2 text-center text-sm text-faint">Arrastrá pasos de la paleta hasta acá, o tocalos para agregarlos.</p>
          ) : null}
          {mode === "ordinal" && steps.length > 0 ? <p className="absolute left-2 top-1 text-xs text-faint">Sin tiempos: corregí los errores para ver la simulación.</p> : null}
          {boxes.map((box) => (
            <StepBox
              key={box.id}
              box={box}
              top={lanesTop + box.lane * LANE_H}
              scale={scale}
              selected={box.ids.includes(selected ?? "")}
              dragging={drag?.blockId === box.id}
              countEditable={isCountEditable(box.id)}
              onPointerDown={(e) => onBoxPointerDown(e, box)}
              onNudge={(direction) => onNudge(box.id, direction)}
              onRemove={() => onRemove(box.id)}
              onSelect={() => onSelect(box.id)}
              onCount={(delta) => onCount(box.id, delta)}
            />
          ))}
          {drag ? (
            <div
              aria-hidden="true"
              className="pointer-events-none absolute rounded-md border-2 border-dashed border-foreground/70"
              style={{ left: drag.ghostX - 24, top: lanesTop + draggedLane * LANE_H - 2, width: 48, height: BOX_H + 4 }}
            />
          ) : null}
        </div>
      </div>
      {drag?.label ? (
        <div
          role="status"
          className="pointer-events-none fixed z-50 max-w-xs rounded-md border border-amber bg-background px-2 py-1 text-xs text-foreground shadow-lg"
          style={{ left: drag.pointer.x + 14, top: drag.pointer.y + 14 }}
        >
          {drag.label}
        </div>
      ) : null}
    </>
  );
}
