"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useCallback, useEffect, useMemo, useRef, useState, useTransition, type PointerEvent as ReactPointerEvent } from "react";
import { publishBuildOrder, updateBuildOrder } from "@/app/actions";
import { loadCoach } from "@/lib/coach/browser";
import { analyzeAsync } from "@/lib/coach/worker";
import { DEBOUNCE_MS, debouncer, defaultPlan, diffGoals, goalLabel, planFromJson, toManual, type AssistedPlan, type ChangeStrip } from "@/lib/editor/assisted";
import type { CoachRuntime } from "@/lib/coach/runtime";
import { allowedRange, canMake, snapDownTo, snapUp } from "@/lib/editor/constraints";
import { brokenBy, isGuarded } from "@/lib/editor/guard";
import { loadGameData, loadMaps, type GameDataFile, type MapInfo, type PaletteItem } from "@/lib/editor/gameData";
import { labelWidthFor, scaleFor, timelineWidth, timeSpan, type PlacedItem } from "@/lib/editor/layout";
import { blockOf, initialState, nextId, reduce, SOURCES, splitBlock, toJson, type EditorAction, type EditorState, type EditorStep, type Source } from "@/lib/editor/model";
import { entityName } from "@/lib/stepText";
import { detectOpening, STANDARD_START, withOpening, type Opening } from "@/lib/editor/opening";
import { allItems, paletteBar, paletteHere } from "@/lib/editor/palette";
import {
  chainLabel,
  detachNewVillager,
  guideLabels,
  insertByNewVillager,
  insertNewStep,
  isBuiltByNewVillager,
  newVillagerBuilds,
  nextVillagerAt,
  removeNewVillagerBuild,
  syncNewVillagerBuilds,
  villagerNumbers,
  type Inserted,
} from "@/lib/editor/villagers";
import { AssistedEditor } from "./AssistedEditor";
import { BoFields } from "./BoFields";
import { EditorSummary } from "./EditorSummary";
import { EditorTimeline } from "./EditorTimeline";
import { GuideList } from "./GuideList";
import { LoadMenu } from "./LoadMenu";
import { Palette } from "./Palette";
import { ChartLegend } from "./ResourceChart";
import { StartCard } from "./StartCard";
import { StepPanel } from "./StepPanel";
import { useNarrow } from "./useNarrow";
import { useTimelineDrag } from "./useTimelineDrag";

/** Un id de BO como los de la app (10 caracteres de un GUID). */
function newBoId() {
  return crypto.randomUUID().replaceAll("-", "").slice(0, 10);
}

/** Una BO nueva en Asistido (D12): la apertura de siempre y los objetivos de ejemplo. */
function newAssisted(state: EditorState): EditorState {
  return reduce(reduce(state, { type: "new", id: newBoId() }), { type: "setAssisted", assisted: defaultPlan() });
}

/** Las acciones después de las cuales los edificios que hace el próximo aldeano vuelven a esperar a su aldeano. */
const KEEPS_BUILDERS = new Set<EditorAction["type"]>(["insert", "move", "remove", "update", "setCount", "toggleWithPrevious", "commit", "setSteps", "setField"]);

/** Cambia los pasos (y pide simular de nuevo) solo si son otros. */
function withSteps(state: EditorState, steps: EditorStep[]): EditorState {
  return steps === state.steps ? state : { ...state, steps, revision: state.revision + 1 };
}

/** Después de una edición, la simulación: el Core corre acá mismo (sincrónico), así el estado nuevo ya trae su análisis. */
function settle(state: EditorState, coach: CoachRuntime | null): EditorState {
  // Una BO asistida se genera fuera de la página (en el worker, ver el efecto de BuildOrderEditor): tarda más.
  if (!coach || state.analyzedRevision >= state.revision || state.bo.assisted !== undefined) return state;
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

  const showNotice = useCallback((message: string) => {
    setNotice(message);
    window.setTimeout(() => setNotice((current) => (current === message ? null : current)), 6000);
  }, []);

  // D12: la franja "Último cambio", lo último que se tocó y el plan del análisis anterior (se borran al cargar otra BO).
  const [strip, setStrip] = useState<ChangeStrip | null>(null);
  const changeRef = useRef("");
  const lastPlanRef = useRef<AssistedPlan | null>(null);

  // El estado de ahora, para comparar antes y después de cada edición (las acciones del mismo evento se encadenan).
  const stateRef = useRef(state);
  useEffect(() => {
    stateRef.current = state;
  }, [state]);
  const dataRef = useRef(data);
  useEffect(() => {
    dataRef.current = data;
  }, [data]);

  /**
   * Aplica una acción y simula el resultado (el Core es sincrónico: el estado nuevo ya trae su análisis). Los edificios
   * que construye el próximo aldeano siguen esperando a ese aldeano (su población cambia con lo que hay antes y con la
   * civ). Una edición que deja la BO rota (un error nuevo, o un paso que deja de arrancar) no se aplica: se explica por qué.
   */
  const dispatch = useCallback(
    (action: EditorAction) => {
      const prev = stateRef.current;
      const before = prev.analysis?.start ?? STANDARD_START;
      const builds = KEEPS_BUILDERS.has(action.type) ? newVillagerBuilds(prev.steps, before) : new Set<string>();
      let next = reduce(prev, action);
      if (builds.size > 0) next = withSteps(next, syncNewVillagerBuilds(next.steps, builds, before));
      next = settle(next, coach);
      const after = next.analysis?.start;
      if (builds.size > 0 && after && after.population !== before.population) {
        next = settle(withSteps(next, syncNewVillagerBuilds(next.steps, builds, after)), coach);
      }
      const reason = isGuarded(action) ? brokenBy(prev.analysis, next.analysis, next.steps, dataRef.current) : null;
      if (reason) {
        showNotice(`No se puede: ${reason}`);
        return false;
      }
      stateRef.current = next;
      setState(next);
      return true;
    },
    [coach, showNotice],
  );

  // El motor, los datos del juego y los mapas; con el motor, la BO que se edita o una nueva.
  useEffect(() => {
    loadCoach()
      .then((runtime) => {
        setCoach(runtime);
        if (!initialJson) {
          setState((prev) => newAssisted(prev));
          return;
        }
        const result = runtime.analyze(initialJson);
        if (result.ok) {
          setStrip(null);
          lastPlanRef.current = null;
          setState((prev) => reduce(prev, { type: "load", analysis: result }));
          if (result.assisted?.regenerated) showNotice("Se recalculó con el generador nuevo.");
        } else showNotice(`No se pudo leer la BO: ${result.error}`);
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
      setStrip(null);
      lastPlanRef.current = null;
      dispatch({ type: "load", analysis: result });
      if (result.assisted?.regenerated) showNotice("Se recalculó con el generador nuevo.");
    },
    [coach, dispatch, showNotice],
  );

  // D12: el modo asistido. Cada cambio genera la BO de nuevo en el worker, juntando los toques seguidos; la franja
  // "Último cambio" compara la hora de los objetivos con la del análisis anterior.
  const plan = useMemo(() => (state.bo.assisted === undefined ? null : planFromJson(state.bo.assisted)), [state.bo.assisted]);
  // Una línea vertical por cada objetivo cumplido en la línea de tiempo.
  const goalMarkers = useMemo(
    () =>
      (state.analysis?.assisted?.goals ?? [])
        .filter((g) => g.end !== null && plan?.goals[g.index])
        .map((g) => ({ t: g.end!, label: goalLabel(plan!.goals[g.index], (unit) => data?.entities[unit]?.made_in), dashed: true })),
    [data, plan, state.analysis],
  );
  const debounce = useRef(debouncer(DEBOUNCE_MS));
  const sequence = useRef(0);
  useEffect(() => {
    if (!plan || state.analyzedRevision >= state.revision) return;
    const revision = state.revision;
    const json = JSON.stringify(toJson({ bo: state.bo, steps: [] }));
    const before = state.analysis;
    debounce.current.schedule(() => {
      const id = ++sequence.current;
      analyzeAsync(json)
        .then((result) => {
          if (id !== sequence.current || !result.ok) return;
          const buildingOf = (unit: string) => dataRef.current?.entities[unit]?.made_in;
          setStrip(diffGoals(before, result, changeRef.current, plan, lastPlanRef.current, buildingOf));
          lastPlanRef.current = plan;
          setState((prev) => reduce(prev, { type: "generated", analysis: result, revision }));
        })
        .catch((error: unknown) => showNotice(`No se pudo generar la BO: ${error instanceof Error ? error.message : String(error)}`));
    });
  }, [plan, showNotice, state.analysis, state.analyzedRevision, state.bo, state.revision]);

  const changePlan = useCallback(
    (next: AssistedPlan, change: string) => {
      changeRef.current = change;
      if (lastPlanRef.current === null && plan) lastPlanRef.current = plan;
      dispatch({ type: "setAssisted", assisted: next });
    },
    [dispatch, plan],
  );
  /** "Pasar a manual": se quedan los pasos generados y se borran los objetivos (no se vuelve atrás). */
  const toManualMode = useCallback(() => {
    if (!state.analysis) return;
    const { bo, steps } = toManual(state.analysis);
    const next = settle({ ...state, bo, steps, revision: state.revision + 1 }, coach);
    stateRef.current = next;
    setState(next);
    setStrip(null);
    lastPlanRef.current = null;
  }, [coach, state]);

  const labels = useMemo(() => new Map(state.analysis?.steps.map((s) => [s.id, s.text]) ?? []), [state.analysis]);
  const label = useCallback((step: EditorStep) => labels.get(step.id) ?? (step.what ? entityName(step.what, step.count) : step.do), [labels]);
  const map = maps.find((m) => m.key === state.bo.map) ?? null;
  const palette = useMemo(() => (data ? paletteBar(data, state.bo.civ, map) : null), [data, state.bo.civ, map]);

  const mode: "time" | "ordinal" = state.analysis?.ok && state.analysis.valid && state.steps.length > 0 ? "time" : "ordinal";
  // En el celular la columna de nombres es angosta y la línea se desplaza de costado si no entra.
  const scale = useMemo(() => {
    const span = timeSpan(state.analysis);
    const labelWidth = labelWidthFor(width);
    return scaleFor(timelineWidth(width, span, labelWidth), span, labelWidth);
  }, [width, state.analysis]);

  const newStep = useCallback((item: PaletteItem): EditorStep => ({ ...item.step, id: nextId(state), after: [], withPrevious: false }), [state]);
  /** Dónde entra un paso agregado sin arrastrar: después de la caja elegida, o al final. */
  const insertIndex = useCallback(() => {
    const chain = state.selected ? blockOf(state.steps, state.selected) : null;
    return chain ? chain.to + 1 : state.steps.length;
  }, [state.selected, state.steps]);
  // La paleta en ese lugar: solo lo que se puede agregar ahí.
  const paletteNow = useMemo(() => (palette && data ? paletteHere(palette, state.steps, insertIndex(), data) : null), [palette, data, state.steps, insertIndex]);
  const paletteAll = useMemo(() => (palette ? allItems(palette) : []), [palette]);

  // El arranque de la partida (con la civ) y lo que hacen los aldeanos iniciales: el primer paso de la BO.
  const start = state.analysis?.start ?? STANDARD_START;
  const opening = useMemo(() => detectOpening(state.steps, start), [state.steps, start]);
  const sources = useMemo(() => (map ? map.sources : SOURCES), [map]);
  // La BO como en las guías: "Aldeanos 4 a 6 → ovejas", "Aldeano 7: campamento maderero → madera".
  const guide = useMemo(() => guideLabels(state.steps, start), [state.steps, start]);
  const builtByNew = useMemo(() => newVillagerBuilds(state.steps, start), [state.steps, start]);
  const textOf = useCallback((step: EditorStep) => guide.get(step.id) ?? label(step), [guide, label]);
  const insertAfter = useMemo(() => {
    const chain = state.selected ? blockOf(state.steps, state.selected) : null;
    return chain ? chainLabel(state.steps.slice(chain.from, chain.to + 1), state.steps, guide, label) : null;
  }, [guide, label, state.selected, state.steps]);

  /** Un paso nuevo de la paleta en ese lugar: sus aldeanos de lo que hay ahí, o un edificio que hace el próximo aldeano. */
  const insertNew = useCallback(
    (steps: EditorStep[], step: EditorStep, index: number): Inserted => insertNewStep(steps, step, index, start, nextId({ bo: state.bo, steps: [...steps, step] }), sources),
    [sources, start, state.bo],
  );
  const normalize = useCallback((steps: EditorStep[]) => syncNewVillagerBuilds(steps, newVillagerBuilds(state.steps, start), start), [start, state.steps]);
  /** Lo que se cuenta cuando el editor decidió algo que no se ve: "Aldeano 7: campamento maderero → madera (sale de los 4...)". */
  const tellInserted = useCallback(
    (inserted: Inserted) => {
      if (inserted.note) showNotice(`${guideLabels(inserted.steps, start).get(inserted.selectId) ?? "Listo"} (${inserted.note}).`);
    },
    [showNotice, start],
  );

  /**
   * Agrega un paso sin arrastrar: en el lugar pedido si se puede, si no en el primero (o último) donde el juego lo
   * deja; si no hay ninguno, explica qué falta.
   */
  const insertAt = useCallback(
    (step: EditorStep, index: number) => {
      if (!data) return;
      const range = allowedRange(state.steps, [step], data, label);
      if (range.lo > range.hi) {
        showNotice(`No se puede: ${range.loReason ?? range.hiReason ?? "no hay lugar para ese paso"}`);
        return;
      }
      const at = snapDownTo(state.steps, Math.min(Math.max(index, range.lo), range.hi));
      const inserted = insertNew(state.steps, step, at);
      if (dispatch({ type: "setSteps", steps: inserted.steps, selected: inserted.selectId })) tellInserted(inserted);
    },
    [data, dispatch, insertNew, label, showNotice, state.steps, tellInserted],
  );
  /** Borrar: un edificio que hace el próximo aldeano se lleva a su aldeano (que vuelve a su tanda si la hay). */
  const removeStep = useCallback(
    (id: string) => {
      const without = removeNewVillagerBuild(state.steps, id);
      if (without) dispatch({ type: "setSteps", steps: without, selected: null });
      else dispatch({ type: "remove", id });
    },
    [dispatch, state.steps],
  );
  /** Quién construye el edificio: el próximo aldeano, uno de una fuente o los sin tarea. */
  const changeBuilder = useCallback(
    (id: string, builder: "new" | Source | null) => {
      const i = state.steps.findIndex((s) => s.id === id);
      if (i < 0) return;
      const isNew = isBuiltByNewVillager(state.steps, i, start);
      if (builder === "new") {
        if (isNew) return;
        const without = state.steps.filter((s) => s.id !== id);
        // Si encabezaba un paso mixto, el que sigue pasa a encabezarlo.
        if (!state.steps[i].withPrevious && without[i]?.withPrevious) without[i] = { ...without[i], withPrevious: false };
        const inserted = insertByNewVillager(without, { ...state.steps[i], withPrevious: false }, i, start, nextId(state), sources);
        if (dispatch({ type: "setSteps", steps: inserted.steps, selected: id })) tellInserted(inserted);
        return;
      }
      if (isNew) dispatch({ type: "setSteps", steps: detachNewVillager(state.steps, id, builder ?? undefined) });
      else dispatch({ type: "update", id, patch: { from: builder ?? undefined } });
    },
    [dispatch, sources, start, state, tellInserted],
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
    insertNew,
    normalize,
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

  const narrow = useNarrow();
  const [sheetOpen, setSheetOpen] = useState(false);
  const selectedStep = state.steps.find((s) => s.id === state.selected) ?? null;
  const selectedIndex = selectedStep ? state.steps.indexOf(selectedStep) : -1;
  const builder = useMemo(() => {
    if (!selectedStep || selectedStep.do !== "build") return null;
    const isNew = builtByNew.has(selectedStep.id);
    const number = isNew ? (villagerNumbers(state.steps, start).get(state.steps[selectedIndex - 1].id)?.first ?? 0) : nextVillagerAt(state.steps, selectedIndex, start);
    return { isNew, number };
  }, [builtByNew, selectedIndex, selectedStep, start, state.steps]);
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

  const palettePanel = (tapOnly: boolean) =>
    paletteNow ? (
      <Palette
        bar={paletteNow.bar}
        all={paletteAll}
        hidden={paletteNow.hidden}
        where={insertAfter}
        onAtEnd={() => dispatch({ type: "select", id: null })}
        onPointerDown={(e, item) => startPaletteDrag(e, newStep(item))}
        onPick={(item) => insertAt(newStep(item), insertIndex())}
        tapOnly={tapOnly}
      />
    ) : null;
  const stepPanel = (inline: boolean) => (
    <StepPanel
      inline={inline}
      step={selectedStep}
      analyzed={analyzedSelected}
      issues={selectedIssues}
      isFirst={selectedIndex === 0}
      availableSources={sources}
      builder={builder}
      title={selectedStep ? (guide.get(selectedStep.id) ?? null) : null}
      onBuilder={(value) => selectedStep && changeBuilder(selectedStep.id, value)}
      onUpdate={(patch) => selectedStep && dispatch({ type: "update", id: selectedStep.id, patch })}
      onCount={(count) => selectedStep && dispatch({ type: "setCount", id: selectedStep.id, count })}
      onToggleGroup={() => selectedStep && dispatch({ type: "toggleWithPrevious", id: selectedStep.id })}
      onMove={(direction) => selectedStep && nudge(selectedStep.id, direction)}
      onRemove={() => selectedStep && removeStep(selectedStep.id)}
    />
  );
  const guideList = (
    <GuideList
      steps={state.steps}
      analysis={state.analysis}
      labels={guide}
      newVillagerBuilds={builtByNew}
      selected={state.selected}
      expanded={narrow}
      panel={stepPanel(true)}
      textOf={textOf}
      countOf={(chain) => (chain.length === 1 && countEditable(chain[0].id) ? chain[0] : null)}
      onSelect={(id) => dispatch({ type: "select", id: state.selected === id && narrow ? null : id })}
      onCount={(id, count) => dispatch({ type: "setCount", id, count })}
      onMove={nudge}
      onRemove={removeStep}
      onAddBelow={() => setSheetOpen(true)}
    />
  );

  if (runtimeError) {
    return (
      <p role="alert" className="text-sm text-red">
        No se pudo cargar el simulador: {runtimeError}
      </p>
    );
  }

  return (
    <div className="flex flex-col gap-4">
      <LoadMenu
        editCode={editCode}
        onLoad={load}
        onNew={() => {
          const next = newAssisted(stateRef.current);
          stateRef.current = next;
          setState(next);
          setStrip(null);
          lastPlanRef.current = null;
        }}
      />
      {!coach || !data ? (
        <p role="status" className="text-sm text-faint">
          Cargando el motor de la app en el navegador…
        </p>
      ) : null}
      {notice ? (
        <p role="alert" className="sticky top-2 z-30 rounded-md border border-amber bg-panel px-3 py-2 text-sm text-amber">
          {notice}
        </p>
      ) : null}
      <BoFields
        bo={state.bo}
        maps={maps.length > 0 ? maps : [{ key: "any", name: "Cualquiera", water: true, sources: [...SOURCES] }]}
        onField={(field, value) => {
          changeRef.current = field === "civ" ? `civ ${value}` : field === "map" ? `mapa ${value}` : field;
          if (field === "civ" && !plan) changeCiv(value);
          else dispatch({ type: "setField", field, value });
        }}
        onTags={(tags) => dispatch({ type: "setTags", tags })}
      />
      <div role="tablist" aria-label="Modo del editor" className="flex gap-2">
        <button
          type="button"
          role="tab"
          aria-selected={plan !== null}
          className={plan ? "button-primary" : "button"}
          disabled={!plan && state.steps.length > 1}
          title={!plan && state.steps.length > 1 ? "Una BO manual no vuelve a asistida: los objetivos no se pueden adivinar desde los pasos." : undefined}
          onClick={() => !plan && dispatch({ type: "setAssisted", assisted: defaultPlan() })}
        >
          Asistido
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={plan === null}
          className={plan ? "button" : "button-primary"}
          onClick={() => {
            if (plan && window.confirm("La BO pasa a manual: se quedan los pasos y se borran los objetivos. No se puede volver atrás.")) toManualMode();
          }}
        >
          Manual
        </button>
      </div>
      {plan && data ? (
        <AssistedEditor
          plan={plan}
          analysis={state.analysis}
          strip={strip}
          pending={state.analyzedRevision < state.revision}
          civ={state.bo.civ}
          data={data}
          narrow={narrow}
          steps={state.steps.map((step) => ({ id: step.id, text: textOf(step), reason: state.analysis?.assisted?.reasons[step.id] ?? null }))}
          onChange={changePlan}
          onManual={toManualMode}
          timeline={
            <EditorTimeline
              steps={state.steps}
              analysis={state.analysis}
              start={start}
              scale={scale}
              mode={mode}
              viewportWidth={width}
              drag={null}
              selected={null}
              timelineRef={timelineRef}
              data={data}
              extraMarkers={goalMarkers}
              onBoxPointerDown={() => {}}
              onNudge={() => {}}
              onRemove={() => {}}
              onSelect={() => {}}
              onCount={() => {}}
              isCountEditable={() => false}
            />
          }
        />
      ) : null}
      {plan ? null : (
        <>
      {paletteNow && !narrow ? palettePanel(false) : null}
      <StartCard start={start} opening={opening} map={map} onOpening={changeOpening} />
      <EditorTimeline
        steps={state.steps}
        analysis={state.analysis}
        start={start}
        scale={scale}
        mode={mode}
        viewportWidth={width}
        drag={drag}
        selected={state.selected}
        timelineRef={timelineRef}
        data={data}
        onBoxPointerDown={(e: ReactPointerEvent<HTMLDivElement>, item: PlacedItem) => {
          const chain = blockOf(state.steps, item.stepId);
          if (chain) startBoxDrag(e, chain.from, chain.to, countEditable(item.stepId) && !item.construction ? item.stepId : undefined);
        }}
        onNudge={nudge}
        onRemove={removeStep}
        onSelect={(id) => dispatch({ type: "select", id })}
        onCount={(id, delta) => {
          const step = state.steps.find((s) => s.id === id);
          if (step) dispatch({ type: "setCount", id, count: step.count + delta });
        }}
        isCountEditable={countEditable}
      />
      <ChartLegend />
      {narrow ? (
        <>
          {guideList}
          {paletteNow ? (
            <div className="sticky bottom-3 z-30 flex justify-center">
              <button type="button" className="button-primary px-4 py-2.5 text-base shadow-lg" onClick={() => setSheetOpen(true)}>
                + Agregar {insertAfter ? "abajo del elegido" : "al final"}
              </button>
            </div>
          ) : null}
          <EditorSummary analysis={state.analysis} />
        </>
      ) : (
        <>
          <div className="grid gap-4 lg:grid-cols-2">
            {stepPanel(false)}
            <EditorSummary analysis={state.analysis} />
          </div>
          {guideList}
        </>
      )}
        </>
      )}
      {!plan && narrow && sheetOpen && paletteNow ? (
        <div className="fixed inset-0 z-40 flex flex-col justify-end bg-black/60" onClick={() => setSheetOpen(false)}>
          <div
            role="dialog"
            aria-modal="true"
            aria-label="Agregar un paso"
            className="flex max-h-[80vh] flex-col gap-2 overflow-y-auto rounded-t-xl border-t border-line bg-background p-3"
            onClick={(e) => e.stopPropagation()}
            onKeyDown={(e) => {
              if (e.key === "Escape") setSheetOpen(false);
            }}
          >
            <div className="flex items-center justify-between gap-2">
              <p className="text-sm font-semibold">Agregar un paso</p>
              <button type="button" className="button" onClick={() => setSheetOpen(false)}>
                Listo
              </button>
            </div>
            {notice ? <p className="rounded-md border border-amber px-3 py-2 text-sm text-amber">{notice}</p> : null}
            {palettePanel(true)}
          </div>
        </div>
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
