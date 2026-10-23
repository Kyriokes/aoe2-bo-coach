"use client";

import { formatClock } from "@/lib/buildOrder";
import type { AnalyzedStep, Issue } from "@/lib/coach/types";
import { SOURCES, type EditorStep, type Source } from "@/lib/editor/model";
import { sourceName } from "@/lib/stepText";

const SOURCE_OPTIONS = SOURCES.map((s) => ({ value: s, label: sourceName(s) }));

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
  if (!step) {
    return (
      <div className="rounded-md border border-line bg-panel p-3 text-sm text-faint">
        Tocá una caja de la línea de tiempo (o un paso de la lista) para editarla. Arrastrala de costado para moverla y para arriba o abajo para cambiar la cantidad (con el teclado: Alt+← y Alt+→, Alt+↑ y Alt+↓; Supr la borra).
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
        {SOURCE_OPTIONS.filter((o) => availableSources.includes(o.value) || o.value === step[field]).map((o) => (
          <option key={o.value} value={o.value}>
            {o.label}
          </option>
        ))}
      </select>
    </label>
  );
  const number = (label: string, value: number | undefined, onChange: (n: number | undefined) => void, min = 1, placeholder = "") => (
    <label className="flex items-center gap-2 text-sm">
      <span className="w-28 text-muted">{label}</span>
      <input
        type="number"
        inputMode="numeric"
        min={min}
        max={200}
        value={value ?? ""}
        placeholder={placeholder}
        onChange={(e) => onChange(e.target.value === "" ? undefined : Math.max(min, Math.min(200, Math.round(Number(e.target.value)))))}
        className="field w-24"
        aria-label={label}
      />
    </label>
  );

  return (
    <div className="flex flex-col gap-2 rounded-md border border-line bg-panel p-3">
      <div className={`flex items-start justify-between gap-2 ${inline ? "hidden" : ""}`}>
        <p className="text-sm font-semibold">{title ?? analyzed?.text ?? step.what ?? step.do}</p>
        <span className="shrink-0 font-mono text-xs text-faint">
          {analyzed?.start != null && analyzed.end != null ? `${formatClock(analyzed.start)} – ${formatClock(analyzed.end)}` : analyzed ? "no arranca" : ""}
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

      {villagers ? number("Aldeanos nuevos", step.count, (n) => onCount(n ?? 1)) : null}
      {villagers ? sourceSelect("Van a", "assign", null) : null}
      {step.do === "train" && !villagers ? number("Cantidad", step.count, (n) => onCount(n ?? 1)) : null}
      {step.do === "build" && builder ? (
        <label className="flex items-center gap-2 text-sm">
          <span className="w-28 text-muted">Quién lo construye</span>
          <select
            value={builder.isNew ? "new" : (step.from ?? "")}
            onChange={(e) => onBuilder(e.target.value === "new" ? "new" : e.target.value ? (e.target.value as Source) : null)}
            className="field"
            aria-label="Quién lo construye"
          >
            {step.what !== "farm" ? <option value="new">El próximo aldeano (el {builder.number})</option> : null}
            <option value="">Los aldeanos sin tarea</option>
            {SOURCE_OPTIONS.filter((o) => availableSources.includes(o.value) || o.value === step.from).map((o) => (
              <option key={o.value} value={o.value}>
                Uno que está en {o.label}
              </option>
            ))}
          </select>
        </label>
      ) : null}
      {builder?.isNew ? <p className="text-xs text-faint">Arranca cuando sale el aldeano {builder.number}.</p> : null}
      {step.do === "build" && !builder?.isNew ? number("Cantidad", step.count, (n) => onCount(n ?? 1)) : null}
      {step.do === "build" && !builder?.isNew ? number("Constructores", step.builders, (n) => onUpdate({ builders: n }), 1, "1") : null}
      {step.do === "build" ? sourceSelect("Después van a", "assign", "se quedan ahí") : null}
      {step.do === "move" ? number("Cuántos", step.count, (n) => onCount(n ?? 1)) : null}
      {step.do === "move" ? sourceSelect("De", "from", "aldeanos sin tarea") : null}
      {step.do === "move" ? sourceSelect("A", "assign", null) : null}
      {builder?.isNew ? null : number("A N de población", step.at_pop, (n) => onUpdate({ at_pop: n }), 1, "no")}
      <label className="flex items-center gap-2 text-sm">
        <span className="w-28 text-muted">Nota</span>
        <input value={step.note ?? ""} onChange={(e) => onUpdate({ note: e.target.value || undefined })} className="field flex-1" aria-label="Nota" />
      </label>
      {builder?.isNew ? null : (
        <label className={`flex items-center gap-2 text-sm ${isFirst ? "opacity-50" : ""}`}>
          <input type="checkbox" checked={step.withPrevious} disabled={isFirst} onChange={onToggleGroup} />
          Junto con el anterior (paso mixto)
        </label>
      )}
      {step.after.length > 0 || step.at_time !== undefined || step.confirm_by ? (
        <p className="text-xs text-faint">
          Se conserva al guardar:{" "}
          {[
            step.after.length > 0 ? `después de ${step.after.join(", ")}` : null,
            step.at_time !== undefined ? `a las ${formatClock(step.at_time)}` : null,
            step.confirm_by ? `confirmado por ${step.confirm_by}` : null,
          ]
            .filter(Boolean)
            .join(" · ")}
        </p>
      ) : null}
      <div className={`flex flex-wrap gap-2 pt-1 ${inline ? "hidden" : ""}`}>
        <button type="button" className="button" onClick={() => onMove(-1)} title="Alt+←">
          ← Mover antes
        </button>
        <button type="button" className="button" onClick={() => onMove(1)} title="Alt+→">
          Mover después →
        </button>
        <button type="button" className="button ml-auto hover:border-red hover:text-red" onClick={onRemove} title="Supr">
          Borrar
        </button>
      </div>
    </div>
  );
}
