"use client";

import { useEffect, useState, type ReactNode } from "react";
import { formatClock } from "@/lib/buildOrder";
import { loadCoach } from "@/lib/coach/browser";
import type { Analysis, AnalysisResult, PlanBlock } from "@/lib/coach/types";
import { Timeline } from "./Timeline";

const BLOCK_TEXT: Record<PlanBlock["reason"], string> = {
  population: "falta población (casas)",
  resources: "faltan recursos",
  producer: "falta el edificio que lo produce",
  villagers: "no hay aldeanos libres",
  age: "todavía no se llegó a la edad",
};

type State = { status: "loading" } | { status: "error"; message: string } | { status: "done"; result: AnalysisResult };

/**
 * Valida, describe y simula una BO con el Core de la app corriendo en el navegador (WebAssembly), y muestra el
 * resultado. <paramref name="onAnalysis"/> recibe el análisis (por ejemplo, para publicar con la Feudal estimada).
 * <paramref name="fallback"/> se ve mientras carga el Core o si no carga (los pasos escritos desde el servidor).
 */
export function BuildOrderView({
  json,
  onAnalysis,
  fallback,
}: {
  json: string;
  onAnalysis?: (result: AnalysisResult) => void;
  fallback?: ReactNode;
}) {
  const [state, setState] = useState<State>({ status: "loading" });

  useEffect(() => {
    let cancelled = false;
    loadCoach()
      .then((coach) => {
        if (cancelled) return;
        const result = coach.analyze(json);
        setState({ status: "done", result });
        onAnalysis?.(result);
      })
      .catch((error: unknown) => {
        if (!cancelled) setState({ status: "error", message: error instanceof Error ? error.message : String(error) });
      });
    return () => {
      cancelled = true;
    };
  }, [json, onAnalysis]);

  if (state.status === "loading")
    return (
      <div className="flex flex-col gap-3">
        <p role="status" className="text-sm text-faint">
          Simulando la BO con el motor de la app…
        </p>
        {fallback}
      </div>
    );
  if (state.status === "error")
    return (
      <div className="flex flex-col gap-3">
        <p role="alert" className="text-sm text-red">
          No se pudo cargar el simulador: {state.message}
        </p>
        {fallback}
      </div>
    );
  if (!state.result.ok)
    return (
      <p role="alert" className="text-sm text-red">
        {state.result.error}
      </p>
    );
  return <AnalysisView analysis={state.result} />;
}

function AnalysisView({ analysis }: { analysis: Analysis }) {
  const blocksByStep = new Map<string, PlanBlock[]>();
  for (const block of analysis.blocks ?? []) {
    if (block.to - block.from >= 5) blocksByStep.set(block.step_id, [...(blocksByStep.get(block.step_id) ?? []), block]);
  }
  const issues = new Map<string, string[]>();
  for (const issue of analysis.issues) {
    const key = issue.step_id ?? "";
    issues.set(key, [...(issues.get(key) ?? []), issue.message]);
  }
  const target = analysis.targets.feudal_click_time;

  return (
    <div className="flex flex-col gap-5">
      <dl className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        <Stat label="Clic de Feudal" value={analysis.feudal_click} target={target} />
        <Stat label="Clic de Castillos" value={analysis.castle_click} target={analysis.targets.castle_click_time} />
        <div className="rounded-md border border-line bg-panel p-3">
          <dt className="text-xs text-faint">Pasos</dt>
          <dd className="text-lg font-semibold">{analysis.steps.length}</dd>
        </div>
        <div className="rounded-md border border-line bg-panel p-3">
          <dt className="text-xs text-faint">Validación</dt>
          <dd className={`text-lg font-semibold ${analysis.valid ? "text-green" : "text-red"}`}>
            {analysis.valid ? (analysis.issues.length > 0 ? "Con avisos" : "Sin problemas") : "Con errores"}
          </dd>
        </div>
      </dl>

      {analysis.issues.length > 0 ? (
        <ul className="flex flex-col gap-1 rounded-md border border-line bg-panel p-3 text-sm">
          {analysis.issues.map((issue, i) => (
            <li key={i} className={issue.severity === "error" ? "text-red" : "text-amber"}>
              {issue.step_id ? <span className="font-mono text-xs text-faint">{issue.step_id} </span> : null}
              {issue.message}
            </li>
          ))}
        </ul>
      ) : null}

      {analysis.timeline ? <Timeline analysis={analysis} /> : null}

      <ol className="flex flex-col divide-y divide-line rounded-md border border-line bg-panel">
        {analysis.steps.map((step, i) => (
          <li key={step.id} className="flex gap-3 px-3 py-2 text-sm">
            <span className="w-6 shrink-0 text-right text-faint">{i + 1}</span>
            <div className="flex-1">
              <p>{step.text}</p>
              {step.note ? <p className="text-xs text-muted">{step.note}</p> : null}
              {(blocksByStep.get(step.id) ?? []).map((b, j) => (
                <p key={j} className="text-xs text-orange">
                  Se traba de {formatClock(b.from)} a {formatClock(b.to)}: {BLOCK_TEXT[b.reason]}
                </p>
              ))}
              {(issues.get(step.id) ?? []).map((message, j) => (
                <p key={j} className="text-xs text-red">
                  {message}
                </p>
              ))}
            </div>
            <span className="shrink-0 font-mono text-xs text-faint">
              {step.start !== null && step.end !== null ? `${formatClock(step.start)} – ${formatClock(step.end)}` : ""}
            </span>
          </li>
        ))}
      </ol>

      {analysis.posture?.text || analysis.posture?.next_age ? (
        <p className="rounded-md border border-line bg-panel p-3 text-sm">
          <span className="text-faint">Al terminar: </span>
          {[analysis.posture.text, analysis.posture.next_age ? `y después, ${analysis.posture.next_age}` : null]
            .filter(Boolean)
            .join(", ")}
          {analysis.posture.derived ? <span className="text-xs text-faint"> (deducido de los pasos)</span> : null}
        </p>
      ) : null}

      {(analysis.branches ?? []).map((branch, i) => (
        <div key={i} className="rounded-md border border-line bg-panel p-3 text-sm">
          <p>
            <span className="font-semibold">{branch.on}</span>
            {branch.before ? <span className="text-faint"> (antes de {branch.before})</span> : null}
            {branch.note ? <span className="text-muted">: {branch.note}</span> : null}
          </p>
          <ol className="mt-1 list-decimal pl-5 text-muted">
            {branch.steps.map((text, j) => (
              <li key={j}>{text}</li>
            ))}
          </ol>
        </div>
      ))}
    </div>
  );
}

function Stat({ label, value, target }: { label: string; value?: number | null; target?: number }) {
  const delta = value != null && target != null ? value - target : null;
  return (
    <div className="rounded-md border border-line bg-panel p-3">
      <dt className="text-xs text-faint">{label}</dt>
      <dd className="text-lg font-semibold">{value != null ? formatClock(value) : "—"}</dd>
      {target != null ? (
        <dd className={`text-xs ${delta !== null && delta > 15 ? "text-red" : "text-green"}`}>objetivo {formatClock(target)}</dd>
      ) : null}
    </div>
  );
}
