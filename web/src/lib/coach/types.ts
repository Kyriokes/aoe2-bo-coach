/** Lo que devuelve el Core (BuildOrderAnalysis.Analyze) para una BO. Mismos nombres que el JSON (snake_case). */

export type Severity = "error" | "warning";

export interface Issue {
  severity: Severity;
  step_id: string | null;
  message: string;
}

export interface AnalyzedStep {
  id: string;
  /** Una línea en español, por ejemplo "Entrenar 6 aldeanos → ovejas". */
  text: string;
  do: "train" | "build" | "research" | "move";
  what: string | null;
  count: number;
  /** Unidades que hace el paso: aldeanos nuevos en los de aldeanos (count es el total en la fuente), count en los demás, 1 en move y research. */
  units: number;
  note: string | null;
  /** Segundos de partida en que el Planner estima que empieza y termina (null si no se simuló). */
  start: number | null;
  end: number | null;
}

export interface PlanBlock {
  step_id: string;
  reason: "population" | "resources" | "producer" | "villagers" | "age";
  from: number;
  to: number;
  missing: "food" | "wood" | "stone" | "gold" | null;
  /** Lo que falta ya viene en camino (la edad se investiga, el edificio se construye, el aldeano está en la cola). */
  expected?: boolean;
}

export interface PlanSample {
  time: number;
  food: number;
  wood: number;
  gold: number;
  stone: number;
  population: number;
  population_cap: number;
  villagers: number;
}

export interface AnalysisFailure {
  ok: false;
  error: string;
}

export interface Analysis {
  ok: true;
  valid: boolean;
  id: string;
  name: string;
  civ: string;
  /** El mapa para el que está pensada (clave de maps.json), o null si sirve para cualquiera (D8). */
  map: string | null;
  author: string | null;
  description: string | null;
  schema_version: number;
  tags: string[];
  targets: { feudal_click_time?: number; castle_click_time?: number };
  /** Con qué arranca la partida para la civ de la BO (lo que usa el simulador). */
  start: GameStart;
  /** Postura de cierre (al terminar los pasos): qué va siempre en cola y a qué edad se apunta; deducida si la BO no la trae. */
  posture?: { text: string | null; next_age: string | null; derived: boolean };
  /** Ramas: pasos que el coach pone primero cuando ve una señal de la partida. */
  branches?: { on: string; before?: string; note: string | null; steps: string[] }[];
  /** La BO normalizada (migrada a la versión actual del formato), lista para guardar. */
  build_order: Record<string, unknown>;
  issues: Issue[];
  steps: AnalyzedStep[];
  // Solo si la BO es válida y se simuló:
  completed?: boolean;
  feudal_click?: number | null;
  castle_click?: number | null;
  imperial_click?: number | null;
  blocks?: PlanBlock[];
  timeline?: PlanSample[];
  /** Los edificios de la partida: los del arranque y cada uno que construye la BO (D8). */
  buildings?: PlanBuilding[];
  /** Cada unidad, tecnología y construcción con su edificio y sus tiempos (D8). */
  jobs?: PlanJob[];
  /** D12: solo si la BO trae `assisted` y se generó (los pasos de arriba son los generados). */
  assisted?: AssistedAnalysis;
}

/** Lo que dice el generador del modo asistido de una BO. */
export interface AssistedAnalysis {
  /** La versión del generador: si cambia, la misma BO puede salir con otros pasos. */
  version: number;
  /** Los pasos guardados eran otros que los que arma el generador de ahora. */
  regenerated: boolean;
  /** El porqué de cada paso generado, por id. */
  reasons: Record<string, string>;
  /** Cada objetivo, por su lugar en la lista: cuándo empieza y se cumple, o por qué no. */
  goals: { index: number; start: number | null; end: number | null; problem: string | null }[];
  problems: string[];
}

export type AnalysisResult = Analysis | AnalysisFailure;

/** El arranque de la partida: recursos, población, los aldeanos sin tarea (los iniciales) y los que ya trabajan. */
export interface GameStart {
  food: number;
  wood: number;
  gold: number;
  stone: number;
  population: number;
  population_cap: number;
  idle_villagers: number;
  /** Los que arrancan trabajando, por fuente (los de más de algunas civs, en las ovejas). */
  villagers: Partial<Record<"sheep" | "hunt" | "berries" | "fish" | "farm" | "wood" | "gold" | "stone", number>>;
  /** Si la civ hace casas (los hunos no). */
  houses: boolean;
}

/** Un edificio: "town_center#1" (el del arranque) o "<paso>#<n>" (el n-ésimo que construye ese paso). */
export interface PlanBuilding {
  id: string;
  key: string;
  step_id: string | null;
  /** Cuándo se terminó; null si estaba al arrancar. */
  built: number | null;
}

/** Un trabajo de la simulación: una unidad o tecnología en su edificio, o una construcción. */
export interface PlanJob {
  step_id: string;
  kind: "produce" | "build";
  what: string;
  building: string | null;
  start: number;
  end: number;
}
