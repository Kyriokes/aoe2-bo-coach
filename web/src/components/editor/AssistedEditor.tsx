"use client";

import { useMemo, useState, type ReactNode } from "react";
import { formatClock } from "@/lib/buildOrder";
import type { Analysis } from "@/lib/coach/types";
import {
  AGES,
  addGoal,
  goalLabel,
  LIMITS,
  moveGoal,
  removeGoal,
  setUntil,
  updateGoal,
  type AssistedGoal,
  type AssistedPlan,
  type ChangeStrip,
} from "@/lib/editor/assisted";
import { canMake } from "@/lib/editor/constraints";
import type { GameDataFile } from "@/lib/editor/gameData";
import { entityName } from "@/lib/stepText";

/**
 * La pestaña Asistido del editor (D12, pantalla A de la spec): a la izquierda lo de fondo y los objetivos en orden de
 * prioridad; a la derecha la franja "Último cambio", la línea de tiempo para mirar y los pasos generados con su porqué.
 * Cada cambio llama a `onChange` con el plan nuevo y una descripción corta ("establos 2 a 3") para la franja.
 */
export function AssistedEditor({
  plan,
  analysis,
  previous,
  strip,
  pending,
  civ,
  data,
  narrow,
  timeline,
  steps,
  onChange,
  onManual,
}: {
  plan: AssistedPlan;
  analysis: Analysis | null;
  /** La hora de cada objetivo en el análisis anterior (por lugar), para la diferencia debajo de cada uno. */
  previous: Map<number, number | null>;
  strip: ChangeStrip | null;
  pending: boolean;
  civ: string;
  data: GameDataFile;
  narrow: boolean;
  timeline: ReactNode;
  steps: { id: string; text: string; reason: string | null }[];
  onChange: (plan: AssistedPlan, change: string) => void;
  onManual: () => void;
}) {
  const buildingOf = (unit: string) => data.entities[unit]?.made_in;
  const goals = analysis?.assisted?.goals ?? [];
  const label = (goal: AssistedGoal) => goalLabel(goal, buildingOf);

  const left = (
    <div className="flex flex-col gap-4">
      <Background plan={plan} onChange={onChange} />
      <section className="flex flex-col gap-2" aria-label="Objetivos">
        <h3 className="text-sm font-semibold">Objetivos, en orden de prioridad</h3>
        <ol className="flex flex-col gap-2">
          {plan.goals.map((goal, i) => {
            const result = goals.find((g) => g.index === i);
            const before = previous.get(i);
            const delta = result?.end != null && before != null ? Math.round(result.end - before) : null;
            return (
              <li key={i} className="rounded-md border border-line bg-panel p-2">
                <div className="flex flex-wrap items-center gap-2">
                  <span className="w-5 text-right text-xs text-faint">{i + 1}</span>
                  <span className="font-medium">{label(goal)}</span>
                  <GoalControls goal={goal} onPatch={(patch, change) => onChange(updateGoal(plan, i, patch), change)} />
                  <span className="ml-auto flex items-center gap-1">
                    <span className="tabular-nums text-sm">{result?.end != null ? formatClock(result.end) : pending ? "…" : "-"}</span>
                    <button type="button" className="button px-2 py-0.5" aria-label={`Subir ${label(goal)}`} disabled={i === 0}
                      onClick={() => onChange(moveGoal(plan, i, i - 1), `${label(goal)} más arriba`)}>
                      ↑
                    </button>
                    <button type="button" className="button px-2 py-0.5" aria-label={`Bajar ${label(goal)}`} disabled={i === plan.goals.length - 1}
                      onClick={() => onChange(moveGoal(plan, i, i + 1), `${label(goal)} más abajo`)}>
                      ↓
                    </button>
                    <button type="button" className="button px-2 py-0.5" aria-label={`Quitar ${label(goal)}`}
                      onClick={() => onChange(removeGoal(plan, i), `sin ${label(goal)}`)}>
                      ✕
                    </button>
                  </span>
                </div>
                {delta !== null && delta !== 0 ? (
                  <p className={`mt-1 text-xs ${delta < 0 ? "text-green" : "text-red"}`}>
                    {delta < 0 ? "se adelantó" : "se atrasó"} {formatClock(Math.abs(delta))}
                  </p>
                ) : null}
                {result?.problem ? (
                  <p role="alert" className="mt-1 text-xs text-red">
                    {result.problem}
                  </p>
                ) : null}
              </li>
            );
          })}
        </ol>
        <AddGoal plan={plan} civ={civ} data={data} onAdd={(goal) => onChange(addGoal(plan, goal), `+ ${label(goal)}`)} />
      </section>
      <button
        type="button"
        className="button self-start"
        onClick={() => {
          if (window.confirm("La BO pasa a manual: se quedan los pasos y se borran los objetivos. No se puede volver atrás.")) onManual();
        }}
      >
        Pasar a manual
      </button>
    </div>
  );

  const right = (
    <div className="flex min-w-0 flex-col gap-4">
      {strip && strip.lines.length > 0 ? (
        <section className="rounded-md border border-amber bg-panel p-3 text-sm" aria-label="Último cambio">
          <p className="font-semibold">Último cambio: {strip.change}</p>
          <ul className="mt-1 flex flex-col gap-0.5">
            {strip.lines.map((line) => (
              <li key={line}>{line}</li>
            ))}
          </ul>
        </section>
      ) : null}
      {analysis?.assisted?.problems.length ? (
        <ul className="text-sm text-red" role="alert">
          {analysis.assisted.problems.map((p) => (
            <li key={p}>{p}</li>
          ))}
        </ul>
      ) : null}
      {timeline}
      <section aria-label="Pasos generados">
        <h3 className="mb-2 text-sm font-semibold">Los pasos{pending ? " (calculando…)" : ""}</h3>
        <ol className="flex flex-col gap-1 text-sm">
          {steps.map((step) => (
            <li key={step.id}>
              {step.text}
              {step.reason ? <span className="text-faint"> · {step.reason}</span> : null}
            </li>
          ))}
        </ol>
      </section>
    </div>
  );

  return narrow ? (
    <div className="flex flex-col gap-4">
      {left}
      {right}
    </div>
  ) : (
    <div className="grid gap-6 lg:grid-cols-[minmax(18rem,26rem)_1fr]">
      {left}
      {right}
    </div>
  );
}

/** "De fondo": la apertura, los aldeanos sin parar, las mejoras eco y las casas (siempre). */
function Background({ plan, onChange }: { plan: AssistedPlan; onChange: (plan: AssistedPlan, change: string) => void }) {
  const [open, setOpen] = useState(false);
  const o = plan.opening;
  const summary = [o.houses ? "2 casas" : null, `${o.sheep} en ovejas`, o.boar ? "jabalí" : null, o.berries === "auto" ? "bayas" : null]
    .filter(Boolean)
    .join(", ");
  const setOpening = (patch: Partial<AssistedPlan["opening"]>, change: string) => onChange({ ...plan, opening: { ...o, ...patch } }, change);
  return (
    <section className="flex flex-col gap-2 rounded-md border border-line bg-panel p-3 text-sm" aria-label="De fondo">
      <h3 className="font-semibold">De fondo</h3>
      <div className="flex items-center gap-2">
        <span>Apertura: {summary}</span>
        <button type="button" className="button ml-auto px-2 py-0.5" onClick={() => setOpen(!open)} aria-expanded={open}>
          {open ? "Listo" : "Cambiar"}
        </button>
      </div>
      {open ? (
        <div className="flex flex-col gap-1 pl-2">
          <label className="flex items-center gap-2">
            <input type="checkbox" checked={o.houses} onChange={(e) => setOpening({ houses: e.target.checked }, e.target.checked ? "con las 2 casas" : "sin las 2 casas")} />
            Las 2 casas con los aldeanos iniciales
          </label>
          <label className="flex items-center gap-2">
            En las ovejas
            <input className="field w-16" type="number" min={0} max={20} value={o.sheep}
              onChange={(e) => setOpening({ sheep: Math.min(20, Math.max(0, Number(e.target.value) || 0)) }, `ovejas a ${e.target.value}`)} />
          </label>
          <label className="flex items-center gap-2">
            <input type="checkbox" checked={o.boar} onChange={(e) => setOpening({ boar: e.target.checked }, e.target.checked ? "con jabalí" : "sin jabalí")} />
            Cazar los jabalíes
          </label>
          <label className="flex items-center gap-2">
            <input type="checkbox" checked={o.berries === "auto"}
              onChange={(e) => setOpening({ berries: e.target.checked ? "auto" : "no" }, e.target.checked ? "con bayas" : "sin bayas")} />
            Bayas cuando haga falta
          </label>
        </div>
      ) : null}
      <label className="flex flex-wrap items-center gap-2">
        Aldeanos sin parar hasta
        <input type="range" min={LIMITS.until[0]} max={LIMITS.until[1]} value={plan.villagers.until} className="min-w-0 flex-1"
          onChange={(e) => onChange(setUntil(plan, Number(e.target.value)), `aldeanos hasta ${e.target.value}`)} />
        <input type="number" min={LIMITS.until[0]} max={LIMITS.until[1]} value={plan.villagers.until} className="field w-16"
          onChange={(e) => onChange(setUntil(plan, Number(e.target.value) || 1), `aldeanos hasta ${e.target.value}`)} />
      </label>
      <label className="flex items-center gap-2">
        <input type="checkbox" checked={plan.eco_upgrades}
          onChange={(e) => onChange({ ...plan, eco_upgrades: e.target.checked }, e.target.checked ? "con mejoras eco" : "sin mejoras eco")} />
        Mejoras eco automáticas
      </label>
      <label className="flex items-center gap-2 text-faint">
        <input type="checkbox" checked disabled />
        Casas (siempre)
      </label>
    </section>
  );
}

/** Los controles de cada objetivo: la edad, lo antes posible o a N de población; las unidades, cantidad y edificios. */
function GoalControls({ goal, onPatch }: { goal: AssistedGoal; onPatch: (patch: Partial<AssistedGoal>, change: string) => void }) {
  const stepper = (value: number, set: (v: number) => void, what: string) => (
    <span className="flex items-center gap-1">
      <button type="button" className="button px-2 py-0.5" aria-label={`Menos ${what}`} onClick={() => set(value - 1)}>
        −
      </button>
      <span className="tabular-nums">{value}</span>
      <button type="button" className="button px-2 py-0.5" aria-label={`Más ${what}`} onClick={() => set(value + 1)}>
        +
      </button>
    </span>
  );
  if (goal.age) {
    return (
      <span className="flex items-center gap-1 text-sm">
        <select className="field" value={goal.at_pop === undefined ? "asap" : "pop"} aria-label="Cuándo"
          onChange={(e) => onPatch({ at_pop: e.target.value === "asap" ? undefined : 22 }, e.target.value === "asap" ? "lo antes posible" : "a 22 de población")}>
          <option value="asap">lo antes posible</option>
          <option value="pop">a N de población</option>
        </select>
        {goal.at_pop !== undefined ? stepper(goal.at_pop, (v) => onPatch({ at_pop: v }, `a ${v} de población`), "población") : null}
      </span>
    );
  }
  if (goal.unit) {
    const count = goal.count ?? 1;
    const buildings = goal.buildings ?? 1;
    return (
      <span className="flex items-center gap-1 text-sm">
        {stepper(count, (v) => onPatch({ count: v }, `${entityName(goal.unit!, 2)} ${count} a ${v}`), "unidades")}
        en
        {stepper(buildings, (v) => onPatch({ buildings: v }, `edificios ${buildings} a ${v}`), "edificios")}
      </span>
    );
  }
  if (goal.town_centers) {
    const n = goal.town_centers;
    return stepper(n, (v) => onPatch({ town_centers: v }, `centros urbanos ${n} a ${v}`), "centros urbanos");
  }
  return null;
}

type Kind = "age" | "unit" | "town_centers" | "tech";

/** "+ Agregar objetivo": edad, unidades, centros urbanos o tecnología, solo lo que la civ puede hacer. */
function AddGoal({ plan, civ, data, onAdd }: { plan: AssistedPlan; civ: string; data: GameDataFile; onAdd: (goal: AssistedGoal) => void }) {
  const [kind, setKind] = useState<Kind | "">("");
  const options = useMemo(() => {
    const entries = Object.entries(data.entities).filter(([key]) => canMake(data, civ, key));
    const byName = (a: [string, unknown], b: [string, unknown]) => entityName(a[0]).localeCompare(entityName(b[0]), "es");
    return {
      age: AGES.filter((age) => !plan.goals.some((g) => g.age === age)).map((age) => ({ key: age, name: entityName(age) })),
      unit: entries.filter(([key, e]) => e.kind === "unit" && key !== "villager").sort(byName).map(([key]) => ({ key, name: entityName(key, 2) })),
      tech: entries.filter(([key, e]) => e.kind === "tech" && !(AGES as readonly string[]).includes(key)).sort(byName).map(([key]) => ({ key, name: entityName(key) })),
    };
  }, [civ, data, plan.goals]);

  return (
    <div className="flex flex-wrap items-center gap-2 text-sm">
      <select className="field" value={kind} aria-label="Agregar objetivo" onChange={(e) => {
        const next = e.target.value as Kind | "";
        if (next === "town_centers") {
          onAdd({ town_centers: 3 });
          setKind("");
        } else {
          setKind(next);
        }
      }}>
        <option value="">+ Agregar objetivo</option>
        <option value="age">Edad</option>
        <option value="unit">Unidades</option>
        <option value="town_centers">Centros urbanos</option>
        <option value="tech">Tecnología</option>
      </select>
      {kind === "age" || kind === "unit" || kind === "tech" ? (
        <select className="field" value="" aria-label="Cuál" onChange={(e) => {
          const key = e.target.value;
          if (!key) return;
          onAdd(kind === "age" ? { age: key as AssistedGoal["age"] } : kind === "unit" ? { unit: key, count: 10, buildings: 1 } : { tech: key });
          setKind("");
        }}>
          <option value="">Elegí…</option>
          {options[kind].map((o) => (
            <option key={o.key} value={o.key}>
              {o.name}
            </option>
          ))}
        </select>
      ) : null}
    </div>
  );
}
