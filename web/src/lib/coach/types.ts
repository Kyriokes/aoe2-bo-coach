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
  author: string | null;
  description: string | null;
  schema_version: number;
  tags: string[];
  targets: { feudal_click_time?: number; castle_click_time?: number };
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
}

export type AnalysisResult = Analysis | AnalysisFailure;
