"use client";

import { useMemo, type PointerEvent as ReactPointerEvent, type RefObject } from "react";
import type { Analysis, GameStart } from "@/lib/coach/types";
import type { GameDataLite } from "@/lib/editor/constraints";
import { BOX_H, LANE_H, LANES_TOP, lanesFor, NARROW_LABEL_W, ORDINAL_W, PAD_R, placeLanes, type PlacedItem, type Scale } from "@/lib/editor/layout";
import type { EditorStep } from "@/lib/editor/model";
import { guideLabels } from "@/lib/editor/villagers";
import { ResourceChart, type Marker } from "./ResourceChart";
import { StepBox } from "./StepBox";
import type { DragState } from "./useTimelineDrag";

/**
 * La línea de tiempo del editor: el stock de recursos arriba y una línea por edificio que produce (con su
 * construcción al principio y lo que produce de a una cosa), más la línea de los aldeanos (casas, granjas, lo que no
 * produce y los movimientos). Con la BO inválida (sin tiempos) cada paso va en la columna de su lugar en la BO.
 * Durante un arrastre dibuja la vista previa con la escala con que empezó, un fantasma bajo el puntero y el cartel.
 * Si la línea mide más que el lugar (celular, o una BO larga en modo ordinal), se desplaza de costado con la columna
 * de nombres fija; en pantallas angostas esa columna muestra solo el ícono.
 */
export function EditorTimeline({
  steps,
  analysis,
  data,
  start,
  scale: committedScale,
  mode: committedMode,
  viewportWidth,
  drag,
  selected,
  timelineRef,
  onBoxPointerDown,
  onNudge,
  onRemove,
  onSelect,
  onCount,
  isCountEditable,
  extraMarkers = [],
}: {
  steps: EditorStep[];
  analysis: Analysis | null;
  data: GameDataLite | null;
  /** El arranque de la civ, para numerar los aldeanos como en las guías. */
  start: GameStart;
  scale: Scale;
  mode: "time" | "ordinal";
  /** El ancho del lugar donde va la línea (si la línea mide más, se desplaza). */
  viewportWidth: number;
  drag: DragState | null;
  selected: string | null;
  timelineRef: RefObject<HTMLDivElement | null>;
  onBoxPointerDown: (e: ReactPointerEvent<HTMLDivElement>, item: PlacedItem) => void;
  onNudge: (stepId: string, direction: -1 | 1) => void;
  onRemove: (stepId: string) => void;
  onSelect: (stepId: string) => void;
  onCount: (stepId: string, delta: 1 | -1) => void;
  isCountEditable: (stepId: string) => boolean;
  /** Líneas verticales de más (D12: la hora de cada objetivo cumplido en el modo asistido). */
  extraMarkers?: Marker[];
}) {
  const scale = drag ? drag.scale : committedScale;
  const mode = drag ? drag.mode : committedMode;
  const shownSteps = drag?.previewSteps ?? steps;
  const shownAnalysis = drag ? (drag.preview ?? analysis) : analysis;
  const lanesTop = mode === "time" ? LANES_TOP : 24;
  const placed = useMemo(
    () => placeLanes(lanesFor(shownSteps, shownAnalysis, data, guideLabels(shownSteps, start)), scale, mode, lanesTop),
    [shownSteps, shownAnalysis, data, start, scale, mode, lanesTop],
  );
  const height = lanesTop + placed.height + 24;
  const innerWidth = mode === "ordinal" ? Math.max(scale.width, scale.padL + shownSteps.length * ORDINAL_W + PAD_R) : scale.width;
  const labelWidth = scale.padL - 8;
  const narrow = labelWidth <= NARROW_LABEL_W;
  const scrolls = innerWidth > viewportWidth;
  const moving = drag?.kind === "move";
  const markers: Marker[] = [
    shownAnalysis?.feudal_click != null ? { t: shownAnalysis.feudal_click, label: "Feudal", dashed: false } : null,
    shownAnalysis?.castle_click != null ? { t: shownAnalysis.castle_click, label: "Castillos", dashed: false } : null,
    shownAnalysis?.targets.feudal_click_time != null ? { t: shownAnalysis.targets.feudal_click_time, label: "objetivo", dashed: true } : null,
    ...extraMarkers,
  ].filter((m): m is Marker => m !== null);
  const selectedChain = placed.items.find((i) => i.stepId === selected)?.chainId ?? null;
  const dragged = moving ? placed.items.find((i) => i.chainId === drag.blockId) : undefined;
  const ghostTop = dragged ? placed.lanes[dragged.lane].top + dragged.row * LANE_H - 2 : lanesTop - 2;

  return (
    <>
      <div
        ref={timelineRef}
        className="relative overflow-x-auto overflow-y-hidden rounded-md border border-line bg-panel"
        style={{ height: height + (scrolls ? 14 : 0) }}
        aria-label="Línea de tiempo de la build order"
      >
        <div className="relative" style={{ width: innerWidth, height }}>
          {mode === "time" ? <ResourceChart timeline={shownAnalysis?.timeline ?? []} scale={scale} height={height} markers={markers} /> : null}
          {placed.lanes.map((lane, i) => (
            <div
              key={lane.id}
              aria-hidden="true"
              className={`absolute border-t border-line/60 ${i % 2 === 1 ? "bg-white/[0.02]" : ""}`}
              style={{ left: 0, top: lane.top, width: innerWidth, height: lane.rows * LANE_H }}
            />
          ))}
          {placed.items.map((item) => (
            <StepBox
              key={item.key}
              box={item}
              top={placed.lanes[item.lane].top + item.row * LANE_H + (LANE_H - BOX_H) / 2}
              scale={scale}
              selected={item.chainId === selectedChain}
              dragging={moving && drag.blockId === item.chainId}
              countEditable={isCountEditable(item.stepId) && !item.construction}
              onPointerDown={(e) => onBoxPointerDown(e, item)}
              onNudge={(direction) => onNudge(item.stepId, direction)}
              onRemove={() => onRemove(item.stepId)}
              onSelect={() => onSelect(item.stepId)}
              onCount={(delta) => onCount(item.stepId, delta)}
            />
          ))}
          {moving ? (
            <div
              aria-hidden="true"
              className="pointer-events-none absolute rounded-md border-2 border-dashed border-foreground/70"
              style={{ left: drag.ghostX - 24, top: ghostTop, width: 48, height: BOX_H + 4 }}
            />
          ) : null}
          {/* La columna con el nombre de cada línea, encima de la grilla (queda fija al hacer scroll en modo ordinal). */}
          <div className="sticky left-0 top-0 h-full bg-panel" style={{ width: labelWidth }}>
            {mode === "time" ? (
              <p className="absolute left-2 text-xs text-faint" style={{ top: 8 }} title="Recursos y población">
                {narrow ? "Stock" : "Recursos y población"}
              </p>
            ) : (
              <p className="absolute left-2 top-1 text-xs text-red" title="Sin tiempos: la BO tiene errores">
                {narrow ? "Errores" : "Sin tiempos: la BO tiene errores"}
              </p>
            )}
            {placed.lanes.map((lane) => (
              <div
                key={lane.id}
                className="absolute left-0 flex items-center gap-1.5 border-t border-line/60 pl-2 pr-1 text-xs text-muted"
                style={{ top: lane.top, width: labelWidth, height: lane.rows * LANE_H }}
                title={lane.label}
              >
                {/* eslint-disable-next-line @next/next/no-img-element -- íconos del juego, ya chicos (64×64) */}
                <img src={`/icons/${lane.icon}.png`} alt={narrow ? lane.label : ""} width={20} height={20} className="shrink-0 rounded-sm" />
                {narrow ? null : <span className="truncate">{lane.label}</span>}
              </div>
            ))}
          </div>
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
