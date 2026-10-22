import type { Analysis } from "../coach/types";

/**
 * El modelo del editor de BO de la web (D8), puro y sin React: la BO y sus pasos en "unidades", las acciones del
 * reductor y la ida y vuelta con el Core.
 *
 * Unidades: en el formato de la app (schema 2) el `count` de un paso de aldeanos con `assign` es el total que tiene
 * que haber en la fuente, como el contador del HUD. Acá se guarda cuántos aldeanos nuevos van a la fuente, que es lo
 * natural para arrastrar y editar; `toJson` manda `schema_version: 1` (donde count son los nuevos) y el Core lo
 * migra a totales (`BuildOrderSerializer.Parse` → `LivePlan.CountsToTotals`). Para cargar una BO en schema 2 se usan
 * los `units` por paso que devuelve el análisis.
 */

export const SOURCES = ["sheep", "hunt", "berries", "fish", "farm", "wood", "gold", "stone"] as const;
export type Source = (typeof SOURCES)[number];

export const STEP_KINDS = ["train", "build", "research", "move"] as const;
export type StepKind = (typeof STEP_KINDS)[number];

export interface EditorStep {
  id: string;
  do: StepKind;
  what?: string;
  /** Unidades: aldeanos nuevos en los pasos de aldeanos; la cantidad en los demás (1 en investigar). */
  count: number;
  assign?: Source;
  from?: Source;
  builders?: number;
  after: string[];
  at_pop?: number;
  at_time?: number;
  confirm_by?: string;
  note?: string;
  /** "Junto con el anterior": forma un paso mixto (grupo) con el paso de arriba. */
  withPrevious: boolean;
  /** El nombre del grupo con que vino la BO, para conservarlo al guardar ("molino1"). */
  groupName?: string;
}

export interface EditorBo {
  id: string;
  name: string;
  civ: string;
  /** Clave de maps.json, o "any". */
  map: string;
  author: string;
  description: string;
  tags: string[];
  /** Lo que el editor no toca y conserva tal cual: objetivos, postura de cierre y ramas. */
  targets?: unknown;
  posture?: unknown;
  branches?: unknown;
}

export interface EditorState {
  bo: EditorBo;
  steps: EditorStep[];
  selected: string | null;
  /** La última simulación confirmada (la de `analyzedRevision`). */
  analysis: Analysis | null;
  /** Sube con cada edición; cuando supera a `analyzedRevision`, hay que volver a analizar. */
  revision: number;
  analyzedRevision: number;
  loadError: string | null;
}

export type EditorAction =
  | { type: "load"; analysis: Analysis }
  | { type: "new"; id: string }
  | { type: "setField"; field: "name" | "civ" | "map" | "author" | "description"; value: string }
  | { type: "setTags"; tags: string[] }
  | { type: "insert"; step: EditorStep; index: number }
  | { type: "move"; blockFrom: number; blockTo: number; index: number }
  | { type: "remove"; id: string }
  | { type: "update"; id: string; patch: Partial<Omit<EditorStep, "id" | "withPrevious">> }
  | { type: "setCount"; id: string; count: number }
  | { type: "toggleWithPrevious"; id: string }
  | { type: "select"; id: string | null }
  | { type: "commit"; steps: EditorStep[]; analysis: Analysis; selected?: string | null }
  | { type: "analyzed"; analysis: Analysis; revision: number }
  | { type: "loadFailed"; message: string };

export const NEW_NAME = "Nueva build order";

export function initialState(): EditorState {
  return {
    bo: { id: "", name: "", civ: "any", map: "any", author: "", description: "", tags: [] },
    steps: [],
    selected: null,
    analysis: null,
    revision: 0,
    analyzedRevision: 0,
    loadError: null,
  };
}

export function isVillagerTarget(step: { do: string; what?: unknown; assign?: unknown }) {
  return step.do === "train" && step.what === "villager" && typeof step.assign === "string";
}

export function isSource(value: unknown): value is Source {
  return typeof value === "string" && (SOURCES as readonly string[]).includes(value);
}

const text = (value: unknown) => (typeof value === "string" ? value : undefined);
const integer = (value: unknown) => (typeof value === "number" && Number.isInteger(value) ? value : undefined);

/** Lee un paso del JSON normalizado del Core; `units` son las unidades que calculó el Core para ese paso. */
function stepFromJson(raw: unknown, units: number | undefined, previous: EditorStep | undefined): EditorStep {
  const s = (typeof raw === "object" && raw !== null ? raw : {}) as Record<string, unknown>;
  const kind = (STEP_KINDS as readonly string[]).includes(s.do as string) ? (s.do as StepKind) : "train";
  const count = integer(s.count) ?? 1;
  const groupName = text(s.group);
  const step: EditorStep = {
    id: text(s.id) ?? "",
    do: kind,
    count: isVillagerTarget({ do: kind, what: s.what, assign: s.assign }) ? (units ?? count) : count,
    after: Array.isArray(s.after) ? s.after.filter((a): a is string => typeof a === "string") : [],
    withPrevious: previous !== undefined && groupName !== undefined && groupName === previous.groupName,
  };
  if (text(s.what) !== undefined) step.what = text(s.what);
  if (isSource(s.assign)) step.assign = s.assign;
  if (isSource(s.from)) step.from = s.from;
  if (integer(s.builders) !== undefined) step.builders = integer(s.builders);
  if (integer(s.at_pop) !== undefined) step.at_pop = integer(s.at_pop);
  if (integer(s.at_time) !== undefined) step.at_time = integer(s.at_time);
  if (text(s.confirm_by) !== undefined) step.confirm_by = text(s.confirm_by);
  if (text(s.note)) step.note = text(s.note);
  if (groupName !== undefined) step.groupName = groupName;
  return step;
}

/** La BO y sus pasos (en unidades) a partir de un análisis del Core. */
export function fromAnalysis(a: Analysis): { bo: EditorBo; steps: EditorStep[] } {
  const body = a.build_order;
  const rawSteps = Array.isArray(body.steps) ? body.steps : [];
  const steps: EditorStep[] = [];
  rawSteps.forEach((raw, i) => steps.push(stepFromJson(raw, a.steps[i]?.units, steps[i - 1])));
  const bo: EditorBo = {
    id: a.id,
    name: a.name,
    civ: a.civ || "any",
    map: a.map ?? "any",
    author: a.author ?? "",
    description: a.description ?? "",
    tags: [...a.tags],
  };
  if (body.targets !== undefined) bo.targets = body.targets;
  if (body.posture !== undefined) bo.posture = body.posture;
  if (body.branches !== undefined) bo.branches = body.branches;
  return { bo, steps };
}

/**
 * Los nombres de grupo de cada paso según las cadenas de "junto con el anterior" (como `ToSteps` del editor de la
 * app): el primero de la cadena conserva el nombre con que vino o toma "g" + su id.
 */
export function groupNames(steps: EditorStep[]): (string | undefined)[] {
  const names: (string | undefined)[] = [];
  for (let i = 0; i < steps.length; i++) {
    if (i > 0 && steps[i].withPrevious) {
      names[i - 1] ??= steps[i - 1].groupName ?? `g${steps[i - 1].id}`;
      names[i] = names[i - 1];
    } else {
      names[i] = undefined;
    }
  }
  return names;
}

/** El JSON que se manda al Core: `schema_version` 1 (count = unidades), lo demás como la app. */
export function toJson(state: Pick<EditorState, "bo" | "steps">): Record<string, unknown> {
  const names = groupNames(state.steps);
  const steps = state.steps.map((step, i) => {
    const json: Record<string, unknown> = { id: step.id, do: step.do };
    if (step.what !== undefined) json.what = step.what;
    json.count = step.count;
    if (step.assign !== undefined) json.assign = step.assign;
    if (step.from !== undefined) json.from = step.from;
    if (step.builders !== undefined) json.builders = step.builders;
    json.after = [...step.after];
    if (step.at_pop !== undefined) json.at_pop = step.at_pop;
    if (step.at_time !== undefined) json.at_time = step.at_time;
    if (step.confirm_by !== undefined) json.confirm_by = step.confirm_by;
    if (names[i] !== undefined) json.group = names[i];
    if (step.note) json.note = step.note;
    return json;
  });
  const { bo } = state;
  const json: Record<string, unknown> = { schema_version: 1, id: bo.id, name: bo.name, civ: bo.civ || "any" };
  if (bo.map && bo.map !== "any") json.map = bo.map;
  if (bo.author.trim()) json.author = bo.author;
  if (bo.description.trim()) json.description = bo.description;
  json.tags = bo.tags;
  json.steps = steps;
  if (bo.targets !== undefined) json.targets = bo.targets;
  if (bo.posture !== undefined) json.posture = bo.posture;
  if (bo.branches !== undefined) json.branches = bo.branches;
  return json;
}

/** Los índices (primero y último, inclusive) de la cadena de "junto con el anterior" que contiene el paso. */
export function blockOf(steps: EditorStep[], id: string): { from: number; to: number } | null {
  const i = steps.findIndex((s) => s.id === id);
  if (i < 0) return null;
  let from = i;
  while (from > 0 && steps[from].withPrevious) from--;
  let to = i;
  while (to + 1 < steps.length && steps[to + 1].withPrevious) to++;
  return { from, to };
}

/** Si en ese índice de inserción se puede meter algo sin partir una cadena. */
export function isBoundary(steps: EditorStep[], index: number) {
  return index <= 0 || index >= steps.length || !steps[index].withPrevious;
}

/** El índice de inserción válido más cercano hacia abajo (el principio de la cadena que se partiría). */
export function snapDown(steps: EditorStep[], index: number) {
  let p = Math.max(0, Math.min(index, steps.length));
  while (!isBoundary(steps, p)) p--;
  return p;
}

/** Los pasos sin la cadena [from..to] y la cadena aparte (con el primero marcado como principio). */
export function splitBlock(steps: EditorStep[], from: number, to: number): { others: EditorStep[]; block: EditorStep[] } {
  const block = steps.slice(from, to + 1).map((s, i) => (i === 0 && s.withPrevious ? { ...s, withPrevious: false } : s));
  const others = [...steps.slice(0, from), ...steps.slice(to + 1)];
  // El que seguía a la cadena pasa a ser principio de la suya (si colgaba de ella).
  if (others[from] && others[from].withPrevious) others[from] = { ...others[from], withPrevious: false };
  return { others, block };
}

/** Inserta la cadena en un borde de `others` (si el índice cae dentro de una cadena, va al borde de abajo). */
export function insertBlock(others: EditorStep[], block: EditorStep[], index: number): EditorStep[] {
  const p = snapDown(others, index);
  return [...others.slice(0, p), ...block, ...others.slice(p)];
}

/** Los ids de todos los pasos: los principales y los de las ramas (los nuevos no pueden repetirlos). */
export function allIds(state: Pick<EditorState, "bo" | "steps">): Set<string> {
  const ids = new Set(state.steps.map((s) => s.id));
  if (Array.isArray(state.bo.branches)) {
    for (const branch of state.bo.branches) {
      const steps = (branch as { steps?: unknown })?.steps;
      if (Array.isArray(steps)) for (const s of steps) if (typeof (s as { id?: unknown })?.id === "string") ids.add((s as { id: string }).id);
    }
  }
  return ids;
}

/** Un id nuevo: "s" + el número que sigue al más alto (como StepIds.Next de la app). */
export function nextId(state: Pick<EditorState, "bo" | "steps">): string {
  const ids = allIds(state);
  let max = 0;
  for (const id of ids) {
    const m = /^s(\d+)$/.exec(id);
    if (m) max = Math.max(max, Number(m[1]));
  }
  let candidate = `s${max + 1}`;
  while (ids.has(candidate)) candidate = `${candidate}b`;
  return candidate;
}

function edited(state: EditorState, changes: Partial<EditorState>): EditorState {
  return { ...state, ...changes, revision: state.revision + 1 };
}

export function reduce(state: EditorState, action: EditorAction): EditorState {
  switch (action.type) {
    case "load": {
      const { bo, steps } = fromAnalysis(action.analysis);
      const revision = state.revision + 1;
      return { ...state, bo, steps, selected: null, analysis: action.analysis, revision, analyzedRevision: revision, loadError: null };
    }
    case "new":
      return edited(state, {
        bo: { id: action.id, name: NEW_NAME, civ: "any", map: "any", author: "", description: "", tags: [] },
        steps: [],
        selected: null,
        analysis: null,
        loadError: null,
      });
    case "setField":
      return edited(state, { bo: { ...state.bo, [action.field]: action.value } });
    case "setTags":
      return edited(state, { bo: { ...state.bo, tags: action.tags } });
    case "insert": {
      const step = { ...action.step, withPrevious: false };
      return edited(state, { steps: insertBlock(state.steps, [step], action.index), selected: step.id });
    }
    case "move": {
      const { others, block } = splitBlock(state.steps, action.blockFrom, action.blockTo);
      return edited(state, { steps: insertBlock(others, block, action.index) });
    }
    case "remove": {
      const i = state.steps.findIndex((s) => s.id === action.id);
      if (i < 0) return state;
      const steps = state.steps.filter((s) => s.id !== action.id).map((s) => (s.after.includes(action.id) ? { ...s, after: s.after.filter((a) => a !== action.id) } : s));
      // Si era el principio de una cadena, el que sigue pasa a serlo (y hereda el nombre del grupo).
      if (steps[i] && steps[i].withPrevious && !state.steps[i].withPrevious) {
        steps[i] = { ...steps[i], withPrevious: false, groupName: state.steps[i].groupName };
      }
      return edited(state, { steps, selected: state.selected === action.id ? null : state.selected });
    }
    case "update":
      return edited(state, { steps: state.steps.map((s) => (s.id === action.id ? { ...s, ...action.patch } : s)) });
    case "setCount":
      return edited(state, { steps: state.steps.map((s) => (s.id === action.id ? { ...s, count: Math.max(1, Math.min(200, Math.round(action.count))) } : s)) });
    case "toggleWithPrevious": {
      const i = state.steps.findIndex((s) => s.id === action.id);
      if (i <= 0) return state;
      const steps = [...state.steps];
      const step = steps[i];
      // Al separarse, el paso arranca una cadena nueva sin nombre heredado; al unirse, toma el de la cadena de arriba.
      steps[i] = { ...step, withPrevious: !step.withPrevious, groupName: undefined };
      return edited(state, { steps });
    }
    case "select":
      return state.selected === action.id ? state : { ...state, selected: action.id };
    case "commit": {
      const revision = state.revision + 1;
      return {
        ...state,
        steps: action.steps,
        analysis: action.analysis,
        revision,
        analyzedRevision: revision,
        selected: action.selected === undefined ? state.selected : action.selected,
      };
    }
    case "analyzed":
      return action.revision < state.analyzedRevision ? state : { ...state, analysis: action.analysis, analyzedRevision: action.revision };
    case "loadFailed":
      return { ...state, loadError: action.message };
  }
}
