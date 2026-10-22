import { describeStep, type StepLike } from "@/lib/stepText";

/** Lo que hace falta de un paso para listarlo. */
export type ListedStep = StepLike & { note?: unknown };

/** Los pasos de la BO (el JSON tal cual), sin los que no son objetos. */
export function stepsOf(body: Record<string, unknown>): ListedStep[] {
  return Array.isArray(body.steps) ? body.steps.filter((s): s is ListedStep => typeof s === "object" && s !== null) : [];
}

/**
 * Los pasos escritos desde el servidor, con el mismo texto que el simulador (describeStep = StepText.Describe):
 * los ven los buscadores y quien abre la página mientras carga el Core en WebAssembly, que después los reemplaza
 * por la versión con tiempos.
 */
export function StepList({ steps }: { steps: ListedStep[] }) {
  if (steps.length === 0) return null;
  return (
    <ol className="flex flex-col divide-y divide-line rounded-md border border-line bg-panel">
      {steps.map((step, i) => (
        <li key={i} className="flex gap-3 px-3 py-2 text-sm">
          <span className="w-6 shrink-0 text-right text-faint">{i + 1}</span>
          <div className="flex-1">
            <p>{describeStep(step)}</p>
            {typeof step.note === "string" && step.note ? <p className="text-xs text-muted">{step.note}</p> : null}
          </div>
        </li>
      ))}
    </ol>
  );
}
