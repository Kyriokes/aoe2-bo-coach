import type { Analysis, PlanBlock, PlanJob } from "../coach/types";
import { entityName, sourceName } from "../stepText";
import type { GameDataLite } from "./constraints";
import { isVillagerTarget, type EditorStep, type StepKind } from "./model";

/**
 * Geometría de la línea de tiempo del editor (D8): una línea por edificio que produce (el centro urbano, Cuartel 1,
 * Galería de tiro 2...), que arranca con la caja de su construcción y sigue con lo que produce de a una cosa, y una
 * línea "Aldeanos" arriba para lo que no produce (casas, granjas, campamentos sin tecnologías) y los movimientos.
 * Puro, en píxeles.
 */

/** Ancho de la columna con el nombre de cada línea; en pantallas angostas, solo el ícono. */
export const LABEL_W = 150;
export const NARROW_LABEL_W = 44;
export const PAD_L = LABEL_W + 8;
export const PAD_R = 8;
/** Debajo de este ancho, la línea de tiempo va con la columna angosta (celular). */
export const NARROW_WIDTH = 640;
/** Lo mínimo que mide un minuto: si no entra, la línea se desplaza de costado (un aldeano de 25 s mide 30 px). */
export const MIN_PX_PER_MIN = 72;
/** Alto del gráfico de recursos. */
export const CHART_H = 110;
export const LANES_TOP = CHART_H + 24;
export const LANE_H = 30;
export const BOX_H = 26;
export const MIN_BOX_PX = 28;
export const GAP_PX = 3;
/** Ancho de cada caja cuando la BO no tiene tiempos (modo ordinal). */
export const ORDINAL_W = 120;
/** Lo mínimo que muestra la línea, en segundos. */
export const MIN_SPAN = 600;
/** La línea de los aldeanos (construcciones que no producen y movimientos). */
export const VILLAGERS_LANE = "aldeanos";

export interface Scale {
  /** Píxeles por segundo. */
  pps: number;
  /** Segundos que entran. */
  span: number;
  width: number;
  /** Donde empieza el tiempo 0 (después de la columna de nombres). */
  padL: number;
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

/** El ancho de la columna de nombres según el ancho que hay. */
export function labelWidthFor(viewport: number) {
  return viewport < NARROW_WIDTH ? NARROW_LABEL_W : LABEL_W;
}

/**
 * Lo que mide la línea: en PC, lo que hay (la BO entera a la vista); en el celular, más si un minuto no llega a
 * MIN_PX_PER_MIN (y entonces se desplaza de costado).
 */
export function timelineWidth(viewport: number, span: number, labelWidth = labelWidthFor(viewport)) {
  if (viewport >= NARROW_WIDTH) return viewport;
  return Math.max(viewport, labelWidth + 8 + (span / 60) * MIN_PX_PER_MIN + PAD_R);
}

export function scaleFor(width: number, span: number, labelWidth = LABEL_W): Scale {
  const padL = labelWidth + 8;
  const usable = Math.max(1, width - padL - PAD_R);
  const pps = usable / span;
  return {
    pps,
    span,
    width,
    padL,
    x: (t) => padL + t * pps,
    t: (x) => (x - padL) / pps,
  };
}

/** Una caja de una línea: un tramo de un paso (o el paso entero). */
export interface LaneItem {
  key: string;
  stepId: string;
  /** El primer paso de la cadena de "junto con el anterior": se arrastra y se elige entera. */
  chainId: string;
  label: string;
  kind: StepKind;
  /** La entidad, para el ícono. */
  what?: string;
  /** La caja de la construcción del edificio de la línea. */
  construction: boolean;
  start: number | null;
  end: number | null;
  /** La espera antes de arrancar (el primer bloqueo del paso), para dibujarla rayada. */
  leadIn: { from: number; to: number; block: PlanBlock } | null;
  blocks: PlanBlock[];
  /** Con la BO simulada, el paso no arranca nunca. */
  neverStarts: boolean;
  /** Modo ordinal: el lugar del paso en la BO. */
  index: number;
}

export interface Lane {
  id: string;
  label: string;
  /** El ícono de la línea (la clave del edificio, o "villager"). */
  icon: string;
  items: LaneItem[];
}

/** Las claves que producen algo según game_data (edificios que aparecen como `made_in`). */
function producers(data: GameDataLite | null): Set<string> {
  return new Set(Object.values(data?.entities ?? {}).flatMap((e) => (e.made_in ? [e.made_in] : [])));
}

/** Edificios que entrenan unidades: tienen línea propia aunque en la BO no produzcan nada. */
function trainers(data: GameDataLite | null): Set<string> {
  return new Set(Object.values(data?.entities ?? {}).flatMap((e) => (e.kind === "unit" && e.made_in ? [e.made_in] : [])));
}

function chainIds(steps: EditorStep[]): Map<string, string> {
  const map = new Map<string, string>();
  let current = "";
  for (const step of steps) {
    if (!step.withPrevious || current === "") current = step.id;
    map.set(step.id, current);
  }
  return map;
}

function unitLabel(step: EditorStep | undefined, what: string, count: number) {
  if (step && isVillagerTarget(step)) return `${count} ${entityName("villager", count)} → ${sourceName(step.assign)}`;
  return `${count} ${entityName(what, count)}`;
}

/** El nombre de cada línea de edificio: "Cuartel" si hay uno, "Galería de tiro 1" y "2" si hay varias. */
function labelsFor(keys: string[]): string[] {
  const totals = new Map<string, number>();
  for (const k of keys) totals.set(k, (totals.get(k) ?? 0) + 1);
  const seen = new Map<string, number>();
  return keys.map((k) => {
    const n = (seen.get(k) ?? 0) + 1;
    seen.set(k, n);
    const name = entityName(k, 1);
    const capital = name[0].toUpperCase() + name.slice(1);
    return (totals.get(k) ?? 0) > 1 ? `${capital} ${n}` : capital;
  });
}

/**
 * Las líneas de la línea de tiempo. Con la BO simulada salen de los trabajos del Core (qué edificio hizo cada cosa);
 * sin simulación (BO con errores), cada paso va a la línea de su edificio por la clave, en el orden de la BO. `labels`
 * son los textos como en las guías ("Aldeanos 4 a 6 → ovejas"); los pasos que no están van con el texto del Core.
 */
export function lanesFor(steps: EditorStep[], analysis: Analysis | null, data: GameDataLite | null, labels?: Map<string, string>): Lane[] {
  const chains = chainIds(steps);
  const byId = new Map(steps.map((s, i) => [s.id, { step: s, index: i }]));
  const texts = new Map(analysis?.steps.map((s) => [s.id, s.text]) ?? []);
  const times = new Map(analysis?.steps.map((s) => [s.id, s]) ?? []);
  const blocksByStep = new Map<string, PlanBlock[]>();
  for (const b of analysis?.blocks ?? []) blocksByStep.set(b.step_id, [...(blocksByStep.get(b.step_id) ?? []), b]);
  const simulated = analysis?.ok === true && analysis.valid && analysis.jobs !== undefined;
  const textOf = (step: EditorStep) => labels?.get(step.id) ?? texts.get(step.id) ?? (step.what ? entityName(step.what, step.count) : step.do);
  const item = (step: EditorStep, extra: Partial<LaneItem>): LaneItem => ({
    key: `${step.id}`,
    stepId: step.id,
    chainId: chains.get(step.id) ?? step.id,
    label: textOf(step),
    kind: step.do,
    what: step.what,
    construction: false,
    start: null,
    end: null,
    leadIn: null,
    blocks: blocksByStep.get(step.id) ?? [],
    neverStarts: false,
    index: byId.get(step.id)?.index ?? 0,
    ...extra,
  });

  const villagers: Lane = { id: VILLAGERS_LANE, label: "Aldeanos", icon: "villager", items: [] };
  const lanes: Lane[] = [];

  if (simulated) {
    const jobs = analysis.jobs ?? [];
    const produced = new Map<string, PlanJob[]>();
    for (const j of jobs) if (j.kind === "produce" && j.building) produced.set(j.building, [...(produced.get(j.building) ?? []), j]);
    const ownLane = trainers(data);
    const buildingLanes = (analysis.buildings ?? []).filter((b) => produced.has(b.id) || ownLane.has(b.key) || b.key === "town_center");
    const labels = labelsFor(buildingLanes.map((b) => b.key));
    const laneOf = new Map<string, Lane>();
    buildingLanes.forEach((b, i) => {
      const lane: Lane = { id: b.id, label: labels[i], icon: b.key, items: [] };
      laneOf.set(b.id, lane);
      lanes.push(lane);
    });

    // Las construcciones: en la línea del edificio (al principio) o, si no produce, en la de los aldeanos (una caja por paso).
    const loose = new Map<string, PlanJob[]>();
    for (const j of jobs.filter((j) => j.kind === "build")) {
      const lane = j.building ? laneOf.get(j.building) : undefined;
      const entry = byId.get(j.step_id);
      if (lane && entry) lane.items.push(item(entry.step, { key: `${j.building}:build`, label: `Construir ${entityName(j.what, 1)}`, construction: true, start: j.start, end: j.end }));
      else loose.set(j.step_id, [...(loose.get(j.step_id) ?? []), j]);
    }
    for (const [stepId, list] of loose) {
      const entry = byId.get(stepId);
      if (!entry) continue;
      villagers.items.push(item(entry.step, { start: Math.min(...list.map((j) => j.start)), end: Math.max(...list.map((j) => j.end)) }));
    }

    // Lo que produce cada edificio, de a una cosa: los trabajos seguidos de un mismo paso van en una caja.
    const unitsPerStep = new Map<string, number>();
    for (const j of jobs) if (j.kind === "produce") unitsPerStep.set(j.step_id, (unitsPerStep.get(j.step_id) ?? 0) + 1);
    for (const [building, list] of produced) {
      const lane = laneOf.get(building);
      if (!lane) continue;
      const sorted = [...list].sort((a, b) => a.start - b.start);
      let current: { job: PlanJob; end: number; count: number } | null = null;
      const flush = () => {
        if (!current) return;
        const entry = byId.get(current.job.step_id);
        if (entry) {
          const whole = current.count === unitsPerStep.get(current.job.step_id);
          lane.items.push(
            item(entry.step, {
              key: `${building}:${current.job.step_id}:${current.job.start}`,
              label: whole ? textOf(entry.step) : unitLabel(entry.step, current.job.what, current.count),
              start: current.job.start,
              end: current.end,
            }),
          );
        }
        current = null;
      };
      for (const j of sorted) {
        if (current && current.job.step_id === j.step_id && j.start - current.end <= 1) {
          current.end = j.end;
          current.count++;
        } else {
          flush();
          current = { job: j, end: j.end, count: 1 };
        }
      }
      flush();
    }

    // Los movimientos y lo que no hizo trabajos: arriba, en la de los aldeanos.
    const placed = new Set([...lanes, villagers].flatMap((l) => l.items.map((i) => i.stepId)));
    for (const [index, step] of steps.entries()) {
      if (placed.has(step.id)) continue;
      const t = times.get(step.id);
      const start = t?.start ?? null;
      if (step.do === "move" || start === null) {
        villagers.items.push(item(step, { start, end: t?.end ?? start, neverStarts: start === null, index }));
      }
    }
  } else {
    // Sin simulación: cada paso en la línea de su edificio por la clave (uno por clave), en el orden de la BO.
    const producing = producers(data);
    const laneOfKey = new Map<string, Lane>();
    const keyLane = (key: string) => {
      let lane = laneOfKey.get(key);
      if (!lane) {
        lane = { id: key, label: labelsFor([key])[0], icon: key, items: [] };
        laneOfKey.set(key, lane);
        lanes.push(lane);
      }
      return lane;
    };
    keyLane("town_center");
    const madeIn = (step: EditorStep) => (step.what ? data?.entities[step.what]?.made_in : undefined);
    const used = new Set(steps.flatMap((s) => (madeIn(s) ? [madeIn(s)!] : [])));
    for (const [index, step] of steps.entries()) {
      const producer = madeIn(step);
      if ((step.do === "train" || step.do === "research") && producer) keyLane(producer).items.push(item(step, { index }));
      else if (step.do === "build" && step.what && producing.has(step.what) && (used.has(step.what) || trainers(data).has(step.what)))
        keyLane(step.what).items.push(item(step, { construction: true, label: textOf(step), index }));
      else villagers.items.push(item(step, { index }));
    }
  }

  // La espera de cada paso va en su primera caja.
  const firstItem = new Map<string, LaneItem>();
  for (const it of [...villagers.items, ...lanes.flatMap((l) => l.items)]) {
    const first = firstItem.get(it.stepId);
    if (!first || (it.start ?? Infinity) < (first.start ?? Infinity)) firstItem.set(it.stepId, it);
  }
  for (const it of firstItem.values()) {
    const block = it.blocks.reduce<PlanBlock | null>((best, b) => (best === null || b.from < best.from ? b : best), null);
    if (block && (it.start === null || block.from < it.start)) it.leadIn = { from: block.from, to: it.start ?? Math.max(block.to, block.from), block };
  }
  for (const lane of [villagers, ...lanes]) lane.items.sort((a, b) => (a.start ?? Infinity) - (b.start ?? Infinity) || a.index - b.index);

  return [villagers, ...lanes];
}

export interface PlacedItem extends LaneItem {
  lane: number;
  /** La fila dentro de la línea (la de los aldeanos puede tener varias si las construcciones se superponen). */
  row: number;
  x0: number;
  x1: number;
}

export interface PlacedLane extends Lane {
  top: number;
  rows: number;
}

/**
 * Las cajas en píxeles. En cada línea, cada caja va en la primera fila donde entra (solo la de los aldeanos llega a
 * tener varias: un edificio hace una cosa a la vez). En modo ordinal, cada paso en la columna de su lugar en la BO.
 */
export function placeLanes(lanes: Lane[], scale: Scale, mode: "time" | "ordinal", top: number): { lanes: PlacedLane[]; items: PlacedItem[]; height: number } {
  const items: PlacedItem[] = [];
  const placedLanes: PlacedLane[] = [];
  let y = top;
  lanes.forEach((lane, laneIndex) => {
    const rowEnds: number[] = [];
    for (const it of lane.items) {
      let x0: number;
      let x1: number;
      if (mode === "ordinal") {
        x0 = scale.padL + it.index * ORDINAL_W;
        x1 = x0 + ORDINAL_W - GAP_PX;
      } else if (it.start === null) {
        x1 = scale.x(scale.span);
        x0 = x1 - MIN_BOX_PX * 2;
      } else {
        const startX = scale.x(it.start);
        x0 = it.leadIn ? Math.min(startX, scale.x(it.leadIn.from)) : startX;
        x1 = Math.max(scale.x(it.end ?? it.start), startX + MIN_BOX_PX);
        x1 = Math.min(x1, scale.x(scale.span));
        x0 = Math.min(x0, x1 - 1);
      }
      let row = rowEnds.findIndex((end) => end + GAP_PX <= x0);
      if (row < 0) {
        row = rowEnds.length;
        rowEnds.push(x1);
      } else {
        rowEnds[row] = x1;
      }
      items.push({ ...it, lane: laneIndex, row, x0, x1 });
    }
    const rows = Math.max(1, rowEnds.length);
    placedLanes.push({ ...lane, top: y, rows });
    y += rows * LANE_H;
  });
  return { lanes: placedLanes, items, height: y - top };
}
