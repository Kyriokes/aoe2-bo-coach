"use client";

import { useCallback, useEffect, useRef, useState, type Dispatch, type PointerEvent as ReactPointerEvent, type RefObject } from "react";
import type { CoachRuntime } from "@/lib/coach/runtime";
import type { Analysis } from "@/lib/coach/types";
import { allowedRange, clampCandidate, indexFromTime, snapToBoundary, type GameDataLite, type Range } from "@/lib/editor/constraints";
import { lastWait, waitReason } from "@/lib/editor/explain";
import { boxesFor, packLanes, type Scale } from "@/lib/editor/layout";
import { insertBlock, splitBlock, toJson, type EditorAction, type EditorState, type EditorStep } from "@/lib/editor/model";
import { DROP_OUTSIDE_TEXT, neverStartsText, notBeforeText, PINNED_TEXT } from "@/lib/editor/text";

/** Más de esto por simulación y el arrastre deja de simular en cada cambio de lugar (simula al soltar). */
export const SLOW_ANALYZE_MS = 150;
/** Lo que hay que moverse para que un toque pase a ser un arrastre. */
const DRAG_THRESHOLD_PX = 4;

export type DragSource = { kind: "box"; from: number; to: number } | { kind: "palette"; step: EditorStep };

export interface DragState {
  source: DragSource;
  others: EditorStep[];
  block: EditorStep[];
  /** El id del primer paso de la caja. */
  blockId: string;
  range: Range;
  /** El índice de inserción candidato (ya dentro del rango). */
  candidate: number;
  previewSteps: EditorStep[];
  /** La simulación con la caja en el lugar candidato, si se pudo hacer. */
  preview: Analysis | null;
  /** El motivo si el puntero se pasó del rango. */
  reason: string | null;
  /** Lo que se le dice al usuario junto al puntero. */
  label: string | null;
  /** Paleta: el puntero está fuera de la línea de tiempo (soltar cancela). */
  outside: boolean;
  pointer: { x: number; y: number };
  /** La x del puntero dentro de la línea (con el scroll), para el fantasma. */
  ghostX: number;
  /** La escala y el modo con que empezó el arrastre: no cambian hasta soltar. */
  scale: Scale;
  mode: "time" | "ordinal";
}

interface Options {
  state: EditorState;
  coach: CoachRuntime | null;
  data: GameDataLite | null;
  scale: Scale;
  mode: "time" | "ordinal";
  timelineRef: RefObject<HTMLDivElement | null>;
  label: (step: EditorStep) => string;
  dispatch: Dispatch<EditorAction>;
  onReject: (message: string) => void;
  onPaletteClick: (step: EditorStep) => void;
  onBoxClick: (id: string) => void;
  /** Una caja nueva de la paleta en su lugar: de dónde salen sus aldeanos con los pasos de antes (placeNew). */
  place: (step: EditorStep, before: EditorStep[]) => EditorStep;
}

interface Pending {
  source: DragSource;
  pointerId: number;
  x0: number;
  y0: number;
  element: HTMLElement;
}

/** Lo que se calcula una vez al empezar el arrastre. */
interface Session {
  others: EditorStep[];
  block: EditorStep[];
  blockId: string;
  range: Range;
  pinned: number;
  starts: (number | null)[];
  ends: (number | null)[];
  /** Modo ordinal: el índice de inserción de cada caja de `others` y su centro en píxeles. */
  ordinal: { from: number; center: number }[];
  candidate: number;
  previewSteps: EditorStep[];
  preview: Analysis | null;
  previewEnabled: boolean;
  outside: boolean;
  scale: Scale;
  mode: "time" | "ordinal";
}

/**
 * El arrastre de las cajas (pointer events, sin librería): de una caja de la línea o de la paleta. Mientras se
 * arrastra, solo cuando cambia el índice candidato se vuelve a simular (vista previa); la escala no cambia.
 */
export function useTimelineDrag(options: Options) {
  const [drag, setDrag] = useState<DragState | null>(null);
  const optionsRef = useRef(options);
  useEffect(() => {
    optionsRef.current = options;
  });
  const pendingRef = useRef<Pending | null>(null);
  const sessionRef = useRef<Session | null>(null);
  const pointerRef = useRef({ x: 0, y: 0 });
  const frameRef = useRef<number | null>(null);
  const slowRef = useRef(false);
  /** Saca los eventos del puntero y del teclado que se registraron al tocar. */
  const detachRef = useRef<(() => void) | null>(null);

  const cleanup = useCallback(() => {
    pendingRef.current = null;
    sessionRef.current = null;
    if (frameRef.current !== null) cancelAnimationFrame(frameRef.current);
    frameRef.current = null;
    detachRef.current?.();
    detachRef.current = null;
    setDrag(null);
  }, []);

  // Si el componente se va con un arrastre en curso, los eventos se sacan igual.
  useEffect(() => () => detachRef.current?.(), []);

  const begin = useCallback((pending: Pending) => {
    const { state, data, scale, mode, label } = optionsRef.current;
    if (!data) return;
    let others: EditorStep[];
    let block: EditorStep[];
    let pinned: number;
    if (pending.source.kind === "box") {
      ({ others, block } = splitBlock(state.steps, pending.source.from, pending.source.to));
      pinned = pending.source.from;
    } else {
      others = state.steps;
      block = [pending.source.step];
      pinned = state.steps.length;
    }
    const byId = new Map(state.analysis?.steps.map((s) => [s.id, s]) ?? []);
    const placed = packLanes(boxesFor(others, null), scale, "ordinal").boxes;
    sessionRef.current = {
      others,
      block,
      blockId: block[0].id,
      range: allowedRange(others, block, data, label),
      pinned,
      starts: others.map((s) => byId.get(s.id)?.start ?? null),
      ends: others.map((s) => byId.get(s.id)?.end ?? null),
      ordinal: placed.map((b) => ({ from: b.from, center: (b.x0 + b.x1) / 2 })),
      candidate: -1,
      previewSteps: state.steps,
      preview: null,
      previewEnabled: mode === "time" && !slowRef.current,
      outside: false,
      scale,
      mode,
    };
  }, []);

  const update = useCallback(() => {
    frameRef.current = null;
    const session = sessionRef.current;
    const pending = pendingRef.current;
    const { state, coach, data, timelineRef } = optionsRef.current;
    if (!session || !pending || !timelineRef.current) return;
    const { scale, mode } = session;
    const rect = timelineRef.current.getBoundingClientRect();
    const pointer = pointerRef.current;
    const localX = pointer.x - rect.left + timelineRef.current.scrollLeft;
    const t = scale.t(localX);
    const margin = 40;
    const outside = pending.source.kind === "palette" && (pointer.y < rect.top - margin || pointer.y > rect.bottom + margin || pointer.x < rect.left - margin || pointer.x > rect.right + margin);

    let raw: number;
    if (mode === "time") {
      raw = snapToBoundary(indexFromTime(t, session.starts), session.others, t, session.starts, session.ends);
    } else {
      const next = session.ordinal.find((b) => b.center > localX);
      raw = next ? next.from : session.others.length;
    }
    const { index, reason } = clampCandidate(raw, session.range, session.pinned);
    session.outside = outside;

    if (index !== session.candidate) {
      session.candidate = index;
      // Una caja de la paleta toma sus aldeanos de lo que hay en ese lugar (sin tarea, madera, la fuente con más gente).
      if (pending.source.kind === "palette") session.block = [optionsRef.current.place(pending.source.step, session.others.slice(0, index))];
      session.previewSteps = insertBlock(session.others, session.block, index);
      session.preview = null;
      if (session.previewEnabled && coach && session.range.lo <= session.range.hi) {
        const started = performance.now();
        const result = coach.analyze(JSON.stringify(toJson({ bo: state.bo, steps: session.previewSteps })));
        const elapsed = performance.now() - started;
        if (elapsed > SLOW_ANALYZE_MS) {
          slowRef.current = true;
          session.previewEnabled = false;
          console.debug(`BO Coach: simular tardó ${Math.round(elapsed)} ms; sin vista previa durante el arrastre.`);
        }
        session.preview = result.ok ? result : null;
      }
    }

    let label = reason;
    if (!label && session.preview) {
      const first = session.preview.steps.find((s) => s.id === session.blockId);
      if (first) {
        if (first.start !== null && t < first.start - 1) label = notBeforeText(first.start, waitReason(session.preview, session.block, data) ?? "arranca después del paso anterior");
        else if (first.start === null && session.preview.valid) label = neverStartsText(waitReason(session.preview, session.block, data));
      }
    }
    if (!label && outside) label = DROP_OUTSIDE_TEXT;

    setDrag({
      source: pending.source,
      others: session.others,
      block: session.block,
      blockId: session.blockId,
      range: session.range,
      candidate: index,
      previewSteps: session.previewSteps,
      preview: session.preview,
      reason,
      label,
      outside,
      pointer: { ...pointer },
      ghostX: localX,
      scale,
      mode,
    });
  }, []);

  const finish = useCallback(() => {
    const session = sessionRef.current;
    const pending = pendingRef.current;
    const { dispatch, onReject, onPaletteClick, onBoxClick, data } = optionsRef.current;
    if (pending && !session) {
      // Un toque sin arrastre: elegir la caja, o agregar el paso de la paleta.
      if (pending.source.kind === "palette") onPaletteClick(pending.source.step);
      else onBoxClick(optionsRef.current.state.steps[pending.source.from].id);
    } else if (session && pending) {
      if (session.outside) {
        // nada: soltar fuera de la línea cancela
      } else if (session.range.lo > session.range.hi) {
        onReject(session.range.loReason ?? session.range.hiReason ?? PINNED_TEXT);
      } else if (session.candidate < 0) {
        // no llegó a moverse: nada que hacer
      } else if (session.preview) {
        const ids = new Set(session.block.map((s) => s.id));
        const error = session.preview.issues.find((i) => i.severity === "error" && i.step_id !== null && ids.has(i.step_id));
        const first = session.preview.steps.find((s) => s.id === session.blockId);
        // Sin arranque por población, "after" o aldeanos no hay lugar posible; sin su edificio productor se deja (la caja queda
        // en rojo hasta que se construya, y el Core lo avisa).
        const neverStarts = session.preview.valid && first !== undefined && first.start === null && lastWait(session.preview, session.block)?.reason !== "producer";
        if (error) onReject(error.message);
        else if (neverStarts) onReject(neverStartsText(waitReason(session.preview, session.block, data)));
        else dispatch({ type: "commit", steps: session.previewSteps, analysis: session.preview, selected: session.blockId });
      } else if (pending.source.kind === "palette") {
        dispatch({ type: "insert", step: session.block[0], index: session.candidate });
      } else {
        dispatch({ type: "move", blockFrom: pending.source.from, blockTo: pending.source.to, index: session.candidate });
      }
    }
    cleanup();
  }, [cleanup]);

  /**
   * Los eventos del puntero mientras hay un toque o un arrastre en curso (la captura los manda al elemento, y suben a
   * window). Se registran en el mismo toque, no en un efecto: así un toque y su suelta inmediata no pierden la suelta.
   */
  const attach = useCallback(() => {
    const onMove = (e: PointerEvent) => {
      const pending = pendingRef.current;
      if (!pending || e.pointerId !== pending.pointerId) return;
      pointerRef.current = { x: e.clientX, y: e.clientY };
      if (!sessionRef.current) {
        if (Math.abs(e.clientX - pending.x0) + Math.abs(e.clientY - pending.y0) <= DRAG_THRESHOLD_PX) return;
        begin(pending);
        if (!sessionRef.current) return;
      }
      frameRef.current ??= requestAnimationFrame(update);
    };
    const onUp = (e: PointerEvent) => {
      if (pendingRef.current && e.pointerId === pendingRef.current.pointerId) finish();
    };
    const onCancel = (e: PointerEvent) => {
      if (pendingRef.current && e.pointerId === pendingRef.current.pointerId) cleanup();
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape" && sessionRef.current) {
        e.preventDefault();
        cleanup();
      }
    };
    window.addEventListener("pointermove", onMove);
    window.addEventListener("pointerup", onUp);
    window.addEventListener("pointercancel", onCancel);
    window.addEventListener("keydown", onKey);
    detachRef.current = () => {
      window.removeEventListener("pointermove", onMove);
      window.removeEventListener("pointerup", onUp);
      window.removeEventListener("pointercancel", onCancel);
      window.removeEventListener("keydown", onKey);
    };
  }, [begin, update, finish, cleanup]);

  const start = useCallback((e: ReactPointerEvent<HTMLElement>, source: DragSource) => {
    if (e.button !== 0 || pendingRef.current) return;
    e.preventDefault();
    try {
      e.currentTarget.setPointerCapture(e.pointerId);
    } catch {
      // Sin captura también anda (los eventos suben a window mientras el puntero esté en la página).
    }
    pendingRef.current = { source, pointerId: e.pointerId, x0: e.clientX, y0: e.clientY, element: e.currentTarget };
    pointerRef.current = { x: e.clientX, y: e.clientY };
    attach();
  }, [attach]);

  const startBoxDrag = useCallback((e: ReactPointerEvent<HTMLElement>, from: number, to: number) => start(e, { kind: "box", from, to }), [start]);
  const startPaletteDrag = useCallback((e: ReactPointerEvent<HTMLElement>, step: EditorStep) => start(e, { kind: "palette", step }), [start]);

  return { drag, startBoxDrag, startPaletteDrag };
}
