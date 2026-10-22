import type { Analysis, PlanBlock } from "../coach/types";
import type { EditorStep, StepKind } from "./model";

/** Geometría de la línea de tiempo del editor (D8): escala, cajas y carriles. Puro, en píxeles. */

export const PAD_L = 8;
export const PAD_R = 8;
/** Alto del gráfico de recursos. */
export const CHART_H = 110;
export const LANES_TOP = CHART_H + 24;
export const LANE_H = 30;
export const BOX_H = 26;
export const MIN_BOX_PX = 56;
export const GAP_PX = 4;
/** Ancho de cada caja cuando la BO no tiene tiempos (modo ordinal). */
export const ORDINAL_W = 150;
/** Lo mínimo que muestra la línea, en segundos. */
export const MIN_SPAN = 600;

export interface Scale {
  /** Píxeles por segundo. */
  pps: number;
  /** Segundos que entran. */
  span: number;
  width: number;
  x(t: number): number;
  t(x: number): number;
}

/** Hasta dónde llega la línea: un minuto después del último paso o bloqueo, y nunca menos de 10 minutos, en minutos enteros. */
export function timeSpan(analysis: Analysis | null): number {
  if (!analysis) return MIN_SPAN;
  let last = 0;
  for (const s of analysis.steps) last = Math.max(last, s.end ?? s.start ?? 0);
  for (const b of analysis.blocks ?? []) last = Math.max(last, b.to);
  return Math.max(MIN_SPAN, Math.ceil((last + 60) / 60) * 60);
}

export function scaleFor(width: number, span: number): Scale {
  const usable = Math.max(1, width - PAD_L - PAD_R);
  const pps = usable / span;
  return {
    pps,
    span,
    width,
    x: (t) => PAD_L + t * pps,
    t: (x) => (x - PAD_L) / pps,
  };
}

export interface Box {
  /** El id del primer paso de la cadena (identifica a la caja). */
  id: string;
  ids: string[];
  /** Índices del primero y el último paso de la cadena. */
  from: number;
  to: number;
  label: string;
  kind: StepKind;
  start: number | null;
  end: number | null;
  /** La espera antes de arrancar (el primer bloqueo del paso), para dibujarla rayada. */
  leadIn: { from: number; to: number; block: PlanBlock } | null;
  blocks: PlanBlock[];
  /** Con tiempos simulados pero sin arranque: el paso nunca empieza en ese lugar. */
  neverStarts: boolean;
}

/** Una caja por cadena de "junto con el anterior", con los tiempos y bloqueos de sus pasos. */
export function boxesFor(steps: EditorStep[], analysis: Analysis | null): Box[] {
  const byId = new Map(analysis?.steps.map((s) => [s.id, s]) ?? []);
  const blocksById = new Map<string, PlanBlock[]>();
  for (const b of analysis?.blocks ?? []) blocksById.set(b.step_id, [...(blocksById.get(b.step_id) ?? []), b]);
  const simulated = analysis?.ok === true && analysis.valid;
  const boxes: Box[] = [];
  for (let i = 0; i < steps.length; i++) {
    if (i > 0 && steps[i].withPrevious && boxes.length > 0) {
      boxes[boxes.length - 1].to = i;
      continue;
    }
    boxes.push({ id: steps[i].id, ids: [], from: i, to: i, label: "", kind: steps[i].do, start: null, end: null, leadIn: null, blocks: [], neverStarts: false });
  }
  for (const box of boxes) {
    const members = steps.slice(box.from, box.to + 1);
    box.ids = members.map((s) => s.id);
    box.label = members.map((s) => byId.get(s.id)?.text ?? fallbackLabel(s)).join(" + ");
    const starts = members.map((s) => byId.get(s.id)?.start ?? null).filter((t): t is number => t !== null);
    const ends = members.map((s) => byId.get(s.id)?.end ?? null).filter((t): t is number => t !== null);
    box.start = starts.length > 0 ? Math.min(...starts) : null;
    box.end = ends.length > 0 ? Math.max(...ends) : box.start;
    box.blocks = members.flatMap((s) => blocksById.get(s.id) ?? []);
    const first = box.blocks.reduce<PlanBlock | null>((best, b) => (best === null || b.from < best.from ? b : best), null);
    if (first && (box.start === null || first.from < box.start)) {
      box.leadIn = { from: first.from, to: box.start ?? Math.max(first.to, first.from), block: first };
    }
    box.neverStarts = simulated && box.start === null;
  }
  return boxes;
}

function fallbackLabel(step: EditorStep) {
  return step.what ? step.what.replaceAll("_", " ") : step.do;
}

export interface PlacedBox extends Box {
  lane: number;
  x0: number;
  x1: number;
}

/**
 * Acomoda las cajas en carriles: cada una va al primer carril donde entra después de la anterior (en orden de
 * pasos). En modo ordinal (sin tiempos) van una al lado de la otra.
 */
export function packLanes(boxes: Box[], scale: Scale, mode: "time" | "ordinal"): { boxes: PlacedBox[]; laneCount: number } {
  const laneEnds: number[] = [];
  const placed = boxes.map((box, i) => {
    let x0: number;
    let x1: number;
    if (mode === "ordinal" || box.start === null) {
      x0 = mode === "ordinal" ? PAD_L + i * ORDINAL_W : Math.max(PAD_L, scale.x(scale.span) - MIN_BOX_PX);
      x1 = mode === "ordinal" ? x0 + ORDINAL_W - GAP_PX : x0 + MIN_BOX_PX;
    } else {
      const startX = scale.x(box.start);
      x0 = box.leadIn ? Math.min(startX, scale.x(box.leadIn.from)) : startX;
      x1 = Math.max(scale.x(box.end ?? box.start), startX + MIN_BOX_PX);
    }
    x1 = Math.min(x1, scale.x(scale.span));
    x0 = Math.min(x0, x1 - 1);
    let lane = laneEnds.findIndex((end) => end + GAP_PX <= x0);
    if (lane < 0) {
      lane = laneEnds.length;
      laneEnds.push(x1);
    } else {
      laneEnds[lane] = x1;
    }
    return { ...box, lane, x0, x1 };
  });
  return { boxes: placed, laneCount: laneEnds.length };
}
