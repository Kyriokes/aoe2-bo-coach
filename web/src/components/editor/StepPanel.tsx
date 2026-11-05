"use client";

import { useState } from "react";
import { fill } from "@/i18n";
import { useI18n } from "@/i18n/client";
import { formatClock } from "@/lib/buildOrder";
import type { AnalyzedStep, Issue } from "@/lib/coach/types";
import { parseStepNumber } from "@/lib/editor/input";
import { SOURCES, type EditorStep, type Source } from "@/lib/editor/model";
import { sourceName } from "@/lib/stepText";

/**
 * Un número que se aplica al salir del campo o con Enter. Aplicarlo en cada tecla simula la BO con cada valor de paso:
 * escribir 15 sobre 12 pasa por 1, y si el 1 no se puede, vuelve a 12 y no hay forma de terminar de escribir.
 */
function NumberField({ label, value, onChange, min, placeholder }: { label: string; value: number | undefined; onChange: (n: number | undefined) => void; min: number; placeholder: string }) {
  const [typing, setTyping] = useState<string | null>(null);
  const commit = () => {
    if (typing === null) return;
    setTyping(null);
    const next = parseStepNumber(typing, min);
    if (next !== value) onChange(next);
  };
  return (
    <label className="flex items-center gap-2 text-sm">
      <span className="w-28 text-muted">{label}</span>
      <input
        type="number"
        inputMode="numeric"
        min={min}
        max={200}
        value={typing ?? value ?? ""}
        placeholder={placeholder}
        onChange={(e) => setTyping(e.target.value)}
        onBlur={commit}
        onKeyDown={(e) => {
          if (e.key === "Enter") commit();
        }}
        className="field w-24"
        aria-label={label}
      />
    </label>
  );
}

/**
 * El paso elegido: sus campos según el tipo, lo que dice el Core de él (texto, tiempos, avisos), "junto con el
 * anterior", mover antes o después (el camino sin arrastrar) y borrar. Lo que el editor no edita (after, at_time,
 * confirm_by) se muestra y se conserva. En un edificio, "Quién lo construye": el próximo aldeano (como en las guías),
 * uno de una fuente o los sin tarea.
 */
export function StepPanel({
  step,
  analyzed,
  issues,
  isFirst,
  availableSources,
  builder,
  title,
  inline = false,
  onBuilder,
  onUpdate,
  onCount,
  onToggleGroup,
  onMove,
  onRemove,
}: {
  step: EditorStep | null;
  analyzed: AnalyzedStep | null;
  issues: Issue[];
  isFirst: boolean;
  availableSources: readonly Source[];
  /** En un edificio: si lo construye el próximo aldeano y el número de ese aldeano. */
  builder: { isNew: boolean; number: number } | null;
  /** El texto del paso como en las guías, si lo hay. */
  title: string | null;
  /** Dentro de una fila de la lista (celular): sin el título ni mover y borrar, que ya están en la fila. */
  inline?: boolean;
  /** "new" = el próximo aldeano; una fuente; null = los sin tarea. */
  onBuilder: (value: "new" | Source | null) => void;
  onUpdate: (patch: Partial<Omit<EditorStep, "id" | "withPrevious">>) => void;
  onCount: (count: number) => void;
  onToggleGroup: () => void;
  onMove: (direction: -1 | 1) => void;
  onRemove: () => void;
}) {
  const { locale, t: dict } = useI18n();
  const t = dict.editor.step;
  const sourceOptions = SOURCES.map((s) => ({ value: s, label: sourceName(s, locale) }));
  if (!step) {
    return (
      <div className="rounded-md border border-line bg-panel p-3 text-sm text-faint">
        {t.empty}
      </div>
    );
  }
  const villagers = step.do === "train" && step.what === "villager";
  const sourceSelect = (label: string, field: "assign" | "from", allowNone: string | null) => (
    <label className="flex items-center gap-2 text-sm">
      <span className="w-28 text-muted">{label}</span>
      <select
        value={step[field] ?? ""}
        onChange={(e) => onUpdate({ [field]: e.target.value ? (e.target.value as Source) : undefined })}
        className="field"
        aria-label={label}
      >
        {allowNone !== null ? <option value="">{allowNone}</option> : null}
        {sourceOptions.filter((o) => availableSources.includes(o.value) || o.value === step[field]).map((o) => (
          <option key={o.value} value={o.value}>
            {o.label}
          </option>
        ))}
      </select>
    </label>
  );
  const number = (label: string, value: number | undefined, onChange: (n: number | undefined) => void, min = 1, placeholder = "") => (
    <NumberField key={`${step.id}:${label}`} label={label} value={value} onChange={onChange} min={min} placeholder={placeholder} />
  );

  return (
    <div className="flex flex-col gap-2 rounded-md border border-line bg-panel p-3">
      <div className={`flex items-start justify-between gap-2 ${inline ? "hidden" : ""}`}>
        <p className="text-sm font-semibold">{title ?? analyzed?.text ?? step.what ?? step.do}</p>
        <span className="shrink-0 font-mono text-xs text-faint">
          {analyzed?.start != null && analyzed.end != null ? `${formatClock(analyzed.start)} – ${formatClock(analyzed.end)}` : analyzed ? t.neverStarts : ""}
        </span>
      </div>
      {issues.length > 0 ? (
        <ul className="text-xs">
          {issues.map((issue, i) => (
            <li key={i} className={issue.severity === "error" ? "text-red" : "text-amber"}>
              {issue.message}
            </li>
          ))}
        </ul>
      ) : null}

      {villagers ? number(t.newVillagers, step.count, (n) => onCount(n ?? 1)) : null}
      {villagers ? sourceSelect(t.goTo, "assign", null) : null}
      {step.do === "train" && !villagers ? number(t.quantity, step.count, (n) => onCount(n ?? 1)) : null}
      {step.do === "build" && builder ? (
        <label className="flex items-center gap-2 text-sm">
          <span className="w-28 text-muted">{t.whoBuilds}</span>
          <select
            value={builder.isNew ? "new" : (step.from ?? "")}
            onChange={(e) => onBuilder(e.target.value === "new" ? "new" : e.target.value ? (e.target.value as Source) : null)}
            className="field"
            aria-label={t.whoBuilds}
          >
            {step.what !== "farm" ? <option value="new">{fill(t.nextVillager, { n: builder.number })}</option> : null}
            <option value="">{t.idleVillagers}</option>
            {sourceOptions.filter((o) => availableSources.includes(o.value) || o.value === step.from).map((o) => (
              <option key={o.value} value={o.value}>
                {fill(t.oneFrom, { source: o.label })}
              </option>
            ))}
          </select>
        </label>
      ) : null}
      {builder?.isNew ? <p className="text-xs text-faint">{fill(t.startsWhen, { n: builder.number })}</p> : null}
      {step.do === "build" && !builder?.isNew ? number(t.quantity, step.count, (n) => onCount(n ?? 1)) : null}
      {step.do === "build" && !builder?.isNew ? number(t.builders, step.builders, (n) => onUpdate({ builders: n }), 1, "1") : null}
      {step.do === "build" ? sourceSelect(t.thenGo, "assign", t.stayThere) : null}
      {step.do === "move" ? number(t.howMany, step.count, (n) => onCount(n ?? 1)) : null}
      {step.do === "move" ? sourceSelect(t.from, "from", t.idle) : null}
      {step.do === "move" ? sourceSelect(t.to, "assign", null) : null}
      {builder?.isNew ? null : number(t.atPop, step.at_pop, (n) => onUpdate({ at_pop: n }), 1, t.no)}
      <label className="flex items-center gap-2 text-sm">
        <span className="w-28 text-muted">{t.note}</span>
        <input value={step.note ?? ""} onChange={(e) => onUpdate({ note: e.target.value || undefined })} className="field flex-1" aria-label={t.note} />
      </label>
      {builder?.isNew ? null : (
        <label className={`flex items-center gap-2 text-sm ${isFirst ? "opacity-50" : ""}`}>
          <input type="checkbox" checked={step.withPrevious} disabled={isFirst} onChange={onToggleGroup} />
          {t.withPrevious}
        </label>
      )}
      {step.after.length > 0 || step.at_time !== undefined || step.confirm_by ? (
        <p className="text-xs text-faint">
          {fill(t.keeps, {
            items: [
              step.after.length > 0 ? fill(t.keepsAfter, { steps: step.after.join(", ") }) : null,
              step.at_time !== undefined ? fill(t.keepsAtTime, { time: formatClock(step.at_time) }) : null,
              step.confirm_by ? fill(t.keepsConfirm, { event: step.confirm_by }) : null,
            ]
              .filter(Boolean)
              .join(" · "),
          })}
        </p>
      ) : null}
      <div className={`flex flex-wrap gap-2 pt-1 ${inline ? "hidden" : ""}`}>
        <button type="button" className="button" onClick={() => onMove(-1)} title="Alt+←">
          {t.moveEarlier}
        </button>
        <button type="button" className="button" onClick={() => onMove(1)} title="Alt+→">
          {t.moveLater}
        </button>
        <button type="button" className="button ml-auto hover:border-red hover:text-red" onClick={onRemove} title={t.deleteKey}>
          {t.delete}
        </button>
      </div>
    </div>
  );
}
