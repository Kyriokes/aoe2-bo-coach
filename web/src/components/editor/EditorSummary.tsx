import { useI18n } from "@/i18n/client";
import type { Analysis } from "@/lib/coach/types";
import { BranchList, IssuesList, PostureLine, Stat } from "../BuildOrderView";

/** Lo que dice el Core de la BO entera: los clics de edad contra los objetivos, los problemas, la postura y las ramas. */
export function EditorSummary({ analysis }: { analysis: Analysis | null }) {
  const { t } = useI18n();
  if (!analysis) return <p className="text-sm text-faint">{t.editor.summaryEmpty}</p>;
  return (
    <div className="flex flex-col gap-3">
      <dl className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        <Stat label={t.sim.feudalClick} value={analysis.feudal_click} target={analysis.targets.feudal_click_time} />
        <Stat label={t.sim.castleClick} value={analysis.castle_click} target={analysis.targets.castle_click_time} />
        <div className="rounded-md border border-line bg-panel p-3">
          <dt className="text-xs text-faint">{t.sim.steps}</dt>
          <dd className="text-lg font-semibold">{analysis.steps.length}</dd>
        </div>
        <div className="rounded-md border border-line bg-panel p-3">
          <dt className="text-xs text-faint">{t.sim.validation}</dt>
          <dd className={`text-lg font-semibold ${analysis.valid ? "text-green" : "text-red"}`}>
            {analysis.valid ? (analysis.issues.length > 0 ? t.sim.withWarnings : t.sim.noProblems) : t.sim.withErrors}
          </dd>
        </div>
      </dl>
      {analysis.valid && analysis.completed === false ? (
        <p className="text-sm text-orange">{t.editor.summaryNotEnding}</p>
      ) : null}
      <IssuesList issues={analysis.issues} />
      <PostureLine posture={analysis.posture} />
      <BranchList branches={analysis.branches} />
    </div>
  );
}
