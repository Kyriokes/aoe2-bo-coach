"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useCallback, useEffect, useMemo, useRef, useState, useTransition, type PointerEvent as ReactPointerEvent } from "react";
import { publishBuildOrder, updateBuildOrder } from "@/app/actions";
import { formatClock } from "@/lib/buildOrder";
import { loadCoach } from "@/lib/coach/browser";
import type { CoachRuntime } from "@/lib/coach/runtime";
import { allowedRange, canMake, snapDownTo, snapUp } from "@/lib/editor/constraints";
import { loadGameData, loadMaps, paletteItems, type GameDataFile, type MapInfo, type PaletteItem } from "@/lib/editor/gameData";
import { scaleFor, timeSpan, type PlacedBox } from "@/lib/editor/layout";
import { blockOf, initialState, nextId, reduce, SOURCES, splitBlock, toJson, type EditorAction, type EditorState, type EditorStep } from "@/lib/editor/model";
import { entityName } from "@/lib/stepText";
import { detectOpening, placeNew, STANDARD_START, withOpening, type Opening } from "@/lib/editor/opening";
import { BoFields } from "./BoFields";
import { EditorSummary } from "./EditorSummary";
import { EditorTimeline } from "./EditorTimeline";
import { LoadMenu } from "./LoadMenu";
import { Palette } from "./Palette";
import { ChartLegend } from "./ResourceChart";
import { StartCard } from "./StartCard";
import { StepPanel } from "./StepPanel";
import { useTimelineDrag } from "./useTimelineDrag";

/** Un id de BO como los de la app (10 caracteres de un GUID). */
function newBoId() {
  return crypto.randomUUID().replaceAll("-", "").slice(0, 10);
}

/** Después de una edición, la simulación: el Core corre acá mismo (sincrónico), así el estado nuevo ya trae su análisis. */
function settle(state: EditorState, coach: CoachRuntime | null): EditorState {
  if (!coach || state.analyzedRevision >= state.revision) return state;
  const result = coach.analyze(JSON.stringify(toJson(state)));
  return result.ok ? reduce(state, { type: "analyzed", analysis: result, revision: state.revision }) : state;
}

/**
 * El editor de BO de la web (D8): datos de la BO, paleta, línea de tiempo arrastrable, el paso elegido y el resumen
 * del Core. Cada cambio se simula con el Core en WebAssembly; publicar y descargar usan la BO normalizada que devuelve.
 */
export function BuildOrderEditor({ loggedIn, editCode, initialJson }: { loggedIn: boolean; editCode?: string; initialJson: string }) {
  const router = useRouter();
  const [state, setState] = useState<EditorState>(initialState);
  const [coach, setCoach] = useState<CoachRuntime | null>(null);
  const [data, setData] = useState<GameDataFile | null>(null);
  const [maps, setMaps] = useState<MapInfo[]>([]);
  const [runtimeError, setRuntimeError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [publishError, setPublishError] = useState<string | null>(null);
  const [pending, startTransition] = useTransition();
  const timelineRef = useRef<HTMLDivElement | null>(null);
  const [width, setWidth] = useState(960);

  /** Aplica una acción y simula el resultado (el Core es sincrónico: el estado nuevo ya trae su análisis). */
  const dispatch = useCallback((action: EditorAction) => setState((prev) => settle(reduce(prev, action), coach)), [coach]);

  const showNotice = useCallback((message: string) => {
    setNotice(message);
    window.setTimeout(() => setNotice((current) => (current === message ? null : current)), 5000);
  }, []);

  // El motor, los datos del juego y los mapas; con el motor, la BO que se edita o una nueva.
  useEffect(() => {
    loadCoach()
      .then((runtime) => {
        setCoach(runtime);
        if (!initialJson) {
          setState((prev) => settle(reduce(prev, { type: "new", id: newBoId() }), runtime));
          return;
        }
        const result = runtime.analyze(initialJson);
        if (result.ok) setState((prev) => reduce(prev, { type: "load", analysis: result }));
        else showNotice(`No se pudo leer la BO: ${result.error}`);
      })
      .catch((error: unknown) => setRuntimeError(error instanceof Error ? error.message : String(error)));
    loadGameData()
      .then(setData)
      .catch((error: unknown) => setRuntimeError(error instanceof Error ? error.message : String(error)));
    loadMaps()
      .then(setMaps)
      .catch(() => setMaps([]));
  }, [initialJson, showNotice]);

  // El ancho de la línea de tiempo (la escala se calcula acá, así el arrastre y la línea usan la misma).
  useEffect(() => {
    const element = timelineRef.current;
    if (!element) return;
    const observer = new ResizeObserver((entries) => {
      const w = Math.round(entries[0].contentRect.width);
      if (w > 0) setWidth(w);
    });
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  const load = useCallback(
    (json: string) => {
      if (!coach) return;
      const result = coach.analyze(json);
      if (!result.ok) {
        showNotice(`No se pudo leer la BO: ${result.error}`);
        return;
      }
      dispatch({ type: "load", analysis: result });
    },
    [coach, dispatch, showNotice],
  );

  const labels = useMemo(() => new Map(state.analysis?.steps.map((s) => [s.id, s.text]) ?? []), [state.analysis]);
  const label = useCallback((step: EditorStep) => labels.get(step.id) ?? (step.what ? entityName(step.what, step.count) : step.do), [labels]);
  const map = maps.find((m) => m.key === state.bo.map) ?? null;
  const palette = useMemo(() => (data ? paletteItems(data, state.bo.civ, map) : []), [data, state.bo.civ, map]);

  const mode: "time" | "ordinal" = state.analysis?.ok && state.analysis.valid && state.steps.length > 0 ? "time" : "ordinal";
  const scale = useMemo(() => scaleFor(width, timeSpan(state.analysis)), [width, state.analysis]);

  const newStep = useCallback((item: PaletteItem): EditorStep => ({ ...item.step, id: nextId(state), after: [], withPrevious: false }), [state]);
  /** Dónde entra un paso agregado sin arrastrar: después de la caja elegida, o al final. */
  const insertIndex = useCallback(() => {
    const chain = state.selected ? blockOf(state.steps, state.selected) : null;
    return chain ? chain.to + 1 : state.steps.length;
  }, [state.selected, state.steps]);

  // El arranque de la partida (con la civ) y lo que hacen los aldeanos iniciales: el primer paso de la BO.
  const start = state.analysis?.start ?? STANDARD_START;
  const opening = useMemo(() => detectOpening(state.steps, start), [state.steps, start]);
  const place = useCallback((step: EditorStep, before: EditorStep[]) => placeNew(step, before, start), [start]);
  /** Agrega un paso sin arrastrar, con los aldeanos de donde haya en ese lugar. */
  const insertAt = useCallback(
    (step: EditorStep, index: number) => dispatch({ type: "insert", step: place(step, state.steps.slice(0, index)), index }),
    [dispatch, place, state.steps],
  );
  const changeOpening = useCallback(
    (next: Opening) => dispatch({ type: "setSteps", steps: withOpening(state.steps, next, start, nextId(state)) }),
    [dispatch, start, state],
  );
  /** Cambiar la civ: si la nueva no hace casas (hunos) y la apertura era de casas, los iniciales van directo. */
  const changeCiv = useCallback(
    (civ: string) => {
      dispatch({ type: "setField", field: "civ", value: civ });
      if (data && opening.kind === "houses" && !canMake(data, civ, "house")) {
        dispatch({ type: "setSteps", steps: withOpening(state.steps, { kind: "direct", source: opening.source }, { ...start, houses: false }, nextId(state)) });
        showNotice("Esa civ no hace casas: los aldeanos iniciales van directo a " + (opening.source === "sheep" ? "las ovejas" : "su recurso") + ".");
      }
    },
    [data, dispatch, opening, showNotice, start, state],
  );

  const { drag, startBoxDrag, startPaletteDrag } = useTimelineDrag({
    state,
    coach,
    data,
    scale,
    mode,
    timelineRef,
    label,
    dispatch,
    onReject: showNotice,
    onPaletteClick: (step) => insertAt(step, insertIndex()),
    onBoxClick: (id) => dispatch({ type: "select", id }),
    place,
  });

  /** Mueve la cadena del paso un lugar (teclado o botones del panel), dentro de lo que las reglas dejan. */
  const nudge = useCallback(
    (id: string, direction: -1 | 1) => {
      if (!data) return;
      const chain = blockOf(state.steps, id);
      if (!chain) return;
      const { others, block } = splitBlock(state.steps, chain.from, chain.to);
      const range = allowedRange(others, block, data, label);
      const target = direction < 0 ? snapDownTo(others, chain.from - 1) : snapUp(others, chain.from + 1);
      if (target === chain.from) return;
      if (target < range.lo) {
        showNotice(range.loReason ?? "No puede ir más antes");
        return;
      }
      if (target > range.hi) {
        showNotice(range.hiReason ?? "No puede ir más después");
        return;
      }
      dispatch({ type: "move", blockFrom: chain.from, blockTo: chain.to, index: target });
    },
    [data, state.steps, label, showNotice, dispatch],
  );

  const selectedStep = state.steps.find((s) => s.id === state.selected) ?? null;
  const selectedIndex = selectedStep ? state.steps.indexOf(selectedStep) : -1;
  const analyzedSelected = state.analysis?.steps.find((s) => s.id === state.selected) ?? null;
  const selectedIssues = state.analysis?.issues.filter((i) => i.step_id === state.selected) ?? [];
  const analysisIsCurrent = state.analysis !== null && state.analyzedRevision >= state.revision;
  const canPublish = loggedIn && analysisIsCurrent && state.analysis!.valid && !pending && !drag;
  const canDownload = analysisIsCurrent && state.analysis!.valid;

  function publish() {
    if (!state.analysis || !analysisIsCurrent) return;
    const json = JSON.stringify(state.analysis.build_order);
    const feudal = state.analysis.feudal_click ?? null;
    const castle = state.analysis.castle_click ?? null;
    setPublishError(null);
    startTransition(async () => {
      const result = editCode ? await updateBuildOrder(editCode, json, feudal, castle) : await publishBuildOrder(json, feudal, castle);
      if (!result.ok) {
        setPublishError(result.error);
        return;
      }
      router.push(`/bo/${editCode ?? (result.value as string)}`);
    });
  }

  function download() {
    if (!state.analysis) return;
    const url = URL.createObjectURL(new Blob([JSON.stringify(state.analysis.build_order, null, 2) + "\n"], { type: "application/json" }));
    const link = document.createElement("a");
    link.href = url;
    link.download = `${(state.bo.name || "build-order").replace(/[\/:*?"<>|]/g, "_")}.json`;
    link.click();
    URL.revokeObjectURL(url);
  }

  const countEditable = (id: string) => {
    const step = state.steps.find((s) => s.id === id);
    return step !== undefined && (step.do === "train" || step.do === "build" || step.do === "move");
  };

  if (runtimeError) {
    return (
      <p role="alert" className="text-sm text-red">
        No se pudo cargar el simulador: {runtimeError}
      </p>
    );
  }

  return (
    <div className="flex flex-col gap-4">
      <LoadMenu editCode={editCode} onLoad={load} onNew={() => dispatch({ type: "new", id: newBoId() })} />
      {!coach || !data ? (
        <p role="status" className="text-sm text-faint">
          Cargando el motor de la app en el navegador…
        </p>
      ) : null}
      {notice ? (
        <p role="alert" className="rounded-md border border-amber bg-panel px-3 py-2 text-sm text-amber">
          {notice}
        </p>
      ) : null}
      <BoFields
        bo={state.bo}
        maps={maps.length > 0 ? maps : [{ key: "any", name: "Cualquiera", water: true, sources: [...SOURCES] }]}
        onField={(field, value) => (field === "civ" ? changeCiv(value) : dispatch({ type: "setField", field, value }))}
        onTags={(tags) => dispatch({ type: "setTags", tags })}
      />
      <Palette groups={palette} onPointerDown={(e, item) => startPaletteDrag(e, newStep(item))} onPick={(item) => insertAt(newStep(item), insertIndex())} />
      <StartCard start={start} opening={opening} map={map} onOpening={changeOpening} />
      <EditorTimeline
        steps={state.steps}
        analysis={state.analysis}
        scale={scale}
        mode={mode}
        drag={drag}
        selected={state.selected}
        timelineRef={timelineRef}
        onBoxPointerDown={(e: ReactPointerEvent<HTMLDivElement>, box: PlacedBox) => startBoxDrag(e, box.from, box.to)}
        onNudge={nudge}
        onRemove={(id) => dispatch({ type: "remove", id })}
        onSelect={(id) => dispatch({ type: "select", id })}
        onCount={(id, delta) => {
          const step = state.steps.find((s) => s.id === id);
          if (step) dispatch({ type: "setCount", id, count: step.count + delta });
        }}
        isCountEditable={countEditable}
      />
      <ChartLegend />
      <div className="grid gap-4 lg:grid-cols-2">
        <StepPanel
          step={selectedStep}
          analyzed={analyzedSelected}
          issues={selectedIssues}
          isFirst={selectedIndex === 0}
          availableSources={map ? map.sources : SOURCES}
          onUpdate={(patch) => selectedStep && dispatch({ type: "update", id: selectedStep.id, patch })}
          onCount={(count) => selectedStep && dispatch({ type: "setCount", id: selectedStep.id, count })}
          onToggleGroup={() => selectedStep && dispatch({ type: "toggleWithPrevious", id: selectedStep.id })}
          onMove={(direction) => selectedStep && nudge(selectedStep.id, direction)}
          onRemove={() => selectedStep && dispatch({ type: "remove", id: selectedStep.id })}
        />
        <EditorSummary analysis={state.analysis} />
      </div>
      {state.steps.length > 0 ? (
        <ol className="flex flex-col divide-y divide-line rounded-md border border-line bg-panel" aria-label="Pasos en orden">
          {state.steps.map((step, i) => {
            const analyzed = state.analysis?.steps.find((s) => s.id === step.id);
            return (
              <li key={step.id}>
                <button
                  type="button"
                  onClick={() => dispatch({ type: "select", id: step.id })}
                  className={`flex w-full gap-3 px-3 py-1.5 text-left text-sm hover:bg-panel-2 ${state.selected === step.id ? "bg-panel-2" : ""}`}
                >
                  <span className="w-6 shrink-0 text-right text-faint">{i + 1}</span>
                  <span className="flex-1">
                    {step.withPrevious ? <span className="text-faint">+ </span> : null}
                    {analyzed?.text ?? label(step)}
                    {step.note ? <span className="text-xs text-muted"> · {step.note}</span> : null}
                  </span>
                  <span className="shrink-0 font-mono text-xs text-faint">
                    {analyzed?.start != null && analyzed.end != null ? `${formatClock(analyzed.start)} – ${formatClock(analyzed.end)}` : ""}
                  </span>
                </button>
              </li>
            );
          })}
        </ol>
      ) : null}
      <div className="flex flex-wrap items-center gap-2">
        <button type="button" className="button" onClick={download} disabled={!canDownload}>
          Descargar .json
        </button>
        <button type="button" className="button-primary ml-auto" onClick={publish} disabled={!canPublish}>
          {pending ? "Publicando…" : editCode ? "Guardar la nueva versión" : "Publicar en la biblioteca"}
        </button>
      </div>
      {!loggedIn ? (
        <p className="text-sm text-faint">
          Para publicarla hace falta{" "}
          <Link href="/login?volver=/subir" className="text-amber hover:underline">
            iniciar sesión
          </Link>
          ; descargarla no.
        </p>
      ) : null}
      {analysisIsCurrent && !state.analysis!.valid ? (
        <p role="alert" className="text-sm text-red">
          Corregí los errores antes de publicar.
        </p>
      ) : null}
      {publishError ? (
        <p role="alert" className="text-sm text-red">
          {publishError}
        </p>
      ) : null}
    </div>
  );
}
