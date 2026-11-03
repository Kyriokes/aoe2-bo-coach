import { fill } from "@/i18n";
import { useI18n } from "@/i18n/client";
import { formatClock } from "@/lib/buildOrder";
import type { Analysis } from "@/lib/coach/types";
import { POPULATION, RESOURCES } from "@/lib/resourceColors";


const ACTION_COLORS: Record<string, string> = {
  train: "var(--blue)",
  build: "var(--green)",
  research: "var(--amber)",
  move: "var(--faint)",
};

const WIDTH = 1000;
const LEFT = 8;
const RIGHT = 8;
const CHART = 110;
const ROW = 16;

/**
 * La simulación del Planner en un SVG: arriba el stock de cada recurso, abajo una barra por paso (de cuándo empieza
 * a cuándo termina) y las líneas de los clics de edad y del objetivo de la BO.
 */
export function Timeline({ analysis }: { analysis: Analysis }) {
  const { t: dict } = useI18n();
  const t = dict.sim;
  const samples = analysis.timeline ?? [];
  const steps = analysis.steps.filter((s) => s.start !== null && s.end !== null);
  const end = Math.max(60, ...steps.map((s) => s.end ?? 0), ...samples.map((s) => s.time));
  const x = (t: number) => LEFT + (t / end) * (WIDTH - LEFT - RIGHT);
  const maxStock = Math.max(100, ...samples.flatMap((s) => RESOURCES.map((r) => s[r.key])));
  const y = (v: number) => 4 + CHART - (v / maxStock) * CHART;
  // La población va con su propia escala (hasta el tope más alto), en azul como en el HUD.
  const maxPop = Math.max(5, ...samples.map((s) => s.population_cap));
  const yPop = (v: number) => 4 + CHART - (v / maxPop) * CHART;
  const barsTop = CHART + 24;
  const height = barsTop + steps.length * ROW + 24;
  const ticks = Array.from({ length: Math.floor(end / 60) + 1 }, (_, i) => i * 60);
  const markers = [
    analysis.feudal_click != null ? { t: analysis.feudal_click, label: t.markerFeudal, dashed: false } : null,
    analysis.castle_click != null ? { t: analysis.castle_click, label: t.markerCastle, dashed: false } : null,
    analysis.targets.feudal_click_time != null ? { t: analysis.targets.feudal_click_time, label: t.markerTarget, dashed: true } : null,
  ].filter((m) => m !== null);

  return (
    <figure className="flex flex-col gap-2">
      {/* En el celular el dibujo no entra legible: mide lo mínimo y se desplaza de costado. */}
      <div className="overflow-x-auto rounded-md border border-line bg-panel">
        <svg viewBox={`0 0 ${WIDTH} ${height}`} className="w-full min-w-[56rem]" role="img" aria-label={t.timelineLabel}>
          {ticks.map((tick) => (
          <g key={tick}>
            <line x1={x(tick)} x2={x(tick)} y1={0} y2={height - 18} stroke="var(--line)" strokeWidth={1} opacity={0.5} />
            <text x={x(tick)} y={height - 5} fontSize={11} fill="var(--faint)" textAnchor="middle">
              {formatClock(tick)}
            </text>
          </g>
        ))}

        {RESOURCES.map((r) => (
          <polyline key={r.key} fill="none" stroke={r.color} strokeWidth={2}
            points={samples.map((s) => `${x(s.time)},${y(s[r.key])}`).join(" ")} />
        ))}
        <polyline fill="none" stroke={POPULATION.color} strokeWidth={1.5} strokeDasharray="4 3" opacity={0.7}
          points={samples.map((s) => `${x(s.time)},${yPop(s.population_cap)}`).join(" ")} />
        <polyline fill="none" stroke={POPULATION.color} strokeWidth={2}
          points={samples.map((s) => `${x(s.time)},${yPop(s.population)}`).join(" ")} />

        {steps.map((s, i) => {
          const top = barsTop + i * ROW;
          const width = Math.max(3, x(s.end!) - x(s.start!));
          return (
            <g key={s.id}>
              <title>{fill(t.from, { text: s.text, from: formatClock(s.start!), to: formatClock(s.end!) })}</title>
              <rect x={x(s.start!)} y={top} width={width} height={ROW - 4} rx={3} fill={ACTION_COLORS[s.do] ?? "var(--muted)"} opacity={0.85} />
              <text x={Math.min(x(s.start!) + 4, WIDTH - 220)} y={top + ROW - 7} fontSize={10} fill="var(--background)">
                {s.text.length > 40 ? `${s.text.slice(0, 39)}…` : s.text}
              </text>
            </g>
          );
        })}

        {markers.map((m) => (
          <g key={m.label}>
            <line x1={x(m.t)} x2={x(m.t)} y1={0} y2={height - 18} stroke={m.dashed ? "var(--muted)" : "var(--red)"}
              strokeWidth={2} strokeDasharray={m.dashed ? "6 4" : undefined} />
            <text x={x(m.t) + 4} y={12} fontSize={11} fill={m.dashed ? "var(--muted)" : "var(--red)"}>
              {`${m.label} ${formatClock(m.t)}`}
            </text>
          </g>
        ))}
        </svg>
      </div>
      <figcaption className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-faint">
        {RESOURCES.map((r) => (
          <span key={r.key} className="flex items-center gap-1">
            <span className="inline-block h-0.5 w-4" style={{ background: r.color }} /> {t[r.key]}
          </span>
        ))}
        <span className="flex items-center gap-1">
          <span className="inline-block h-0.5 w-4" style={{ background: POPULATION.color }} /> {t.population} {t.dottedCap}
        </span>
        <span>{t.barsLegend}</span>
      </figcaption>
    </figure>
  );
}
