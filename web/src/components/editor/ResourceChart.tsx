import { formatClock } from "@/lib/buildOrder";
import type { PlanSample } from "@/lib/coach/types";
import { CHART_H, type Scale } from "@/lib/editor/layout";

const RESOURCES = [
  { key: "food", label: "Comida", color: "var(--orange)" },
  { key: "wood", label: "Madera", color: "var(--green)" },
  { key: "gold", label: "Oro", color: "var(--amber)" },
  { key: "stone", label: "Piedra", color: "var(--muted)" },
] as const;

export interface Marker {
  t: number;
  label: string;
  dashed: boolean;
}

/**
 * El stock de cada recurso arriba de la línea de tiempo del editor, con los ticks de cada minuto (que bajan por los
 * carriles) y los marcadores de los clics de edad. Es el gráfico de Timeline.tsx en píxeles reales, para que las
 * cajas (HTML) se ubiquen con la misma escala.
 */
export function ResourceChart({ timeline, scale, height, markers }: { timeline: PlanSample[]; scale: Scale; height: number; markers: Marker[] }) {
  const samples = timeline.filter((t) => t.time <= scale.span);
  const maxStock = Math.max(100, ...samples.flatMap((t) => RESOURCES.map((r) => t[r.key])));
  const y = (v: number) => 4 + CHART_H - (v / maxStock) * CHART_H;
  const ticks = Array.from({ length: Math.floor(scale.span / 60) + 1 }, (_, i) => i * 60);
  return (
    <svg width={scale.width} height={height} className="absolute inset-0" aria-hidden="true">
      {ticks.map((t) => (
        <g key={t}>
          <line x1={scale.x(t)} x2={scale.x(t)} y1={0} y2={height - 18} stroke="var(--line)" strokeWidth={1} opacity={0.5} />
          <text x={scale.x(t)} y={height - 5} fontSize={11} fill="var(--faint)" textAnchor="middle">
            {formatClock(t)}
          </text>
        </g>
      ))}
      <text x={scale.x(0) + 4} y={12} fontSize={11} fill="var(--faint)">
        {maxStock}
      </text>
      <text x={scale.x(0) + 4} y={CHART_H + 2} fontSize={11} fill="var(--faint)">
        0
      </text>
      {RESOURCES.map((r) => (
        <polyline key={r.key} fill="none" stroke={r.color} strokeWidth={2} points={samples.map((t) => `${scale.x(t.time)},${y(t[r.key])}`).join(" ")} />
      ))}
      {markers.map((m) => (
        <g key={m.label}>
          <line x1={scale.x(m.t)} x2={scale.x(m.t)} y1={0} y2={height - 18} stroke={m.dashed ? "var(--muted)" : "var(--red)"} strokeWidth={2} strokeDasharray={m.dashed ? "6 4" : undefined} />
          <text x={scale.x(m.t) + 4} y={12} fontSize={11} fill={m.dashed ? "var(--muted)" : "var(--red)"}>
            {`${m.label} ${formatClock(m.t)}`}
          </text>
        </g>
      ))}
    </svg>
  );
}

export function ChartLegend() {
  return (
    <p className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-faint">
      {RESOURCES.map((r) => (
        <span key={r.key} className="flex items-center gap-1">
          <span className="inline-block h-0.5 w-4" style={{ background: r.color }} /> {r.label}
        </span>
      ))}
      <span>Cajas: aldeanos y unidades (celeste), edificios (verde), investigaciones (ámbar), mover (gris). Rayado: espera.</span>
    </p>
  );
}
