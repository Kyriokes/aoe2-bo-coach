import type { Analysis } from "@/lib/coach/types";
import { BranchList, IssuesList, PostureLine, Stat } from "../BuildOrderView";

/** Lo que dice el Core de la BO entera: los clics de edad contra los objetivos, los problemas, la postura y las ramas. */
export function EditorSummary({ analysis }: { analysis: Analysis | null }) {
  if (!analysis) return <p className="text-sm text-faint">Todavía no hay nada que simular.</p>;
  return (
    <div className="flex flex-col gap-3">
      <dl className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        <Stat label="Clic de Feudal" value={analysis.feudal_click} target={analysis.targets.feudal_click_time} />
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
      {analysis.valid && analysis.completed === false ? (
        <p className="text-sm text-orange">La BO no termina: algún paso nunca arranca (está marcado en rojo en la línea de tiempo).</p>
      ) : null}
      <IssuesList issues={analysis.issues} />
      <PostureLine posture={analysis.posture} />
      <BranchList branches={analysis.branches} />
    </div>
  );
}
