"use client";

import { useState } from "react";
import { chanceText, isEven, percent, type Cell } from "@/lib/matchups";
import type { Choice } from "./MatchupRivals";

const WIDTH = 640;
const TOP = 16;
const PLOT = 200;
const LEFT = 44;
const RIGHT = 12;
const BOTTOM = 52;
const LOW = 0.2;
const HIGH = 0.8;

/**
 * La chance de un cruce según cuánto duró la partida (una columna por franja): el punto es la chance, la barra el
 * rango probable del 90 % y debajo las partidas. Mapa y franja de Elo para elegir (D10, spec 5.5).
 */
export function MatchupDuration({
  cells,
  civName,
  rivalName,
  maps,
  elos,
  durations,
}: {
  /** Los 720 enteros del cruce desde la civ de la página (milésimas y partidas). */
  cells: number[];
  civName: string;
  rivalName: string;
  maps: Choice[];
  elos: Choice[];
  /** Las etiquetas de las franjas 1 a 5 ("menos de 20 min", ...). */
  durations: string[];
}) {
  const [map, setMap] = useState(0);
  const [elo, setElo] = useState(0);
  const at = (duration: number): Cell => {
    const k = ((map * 6 + elo) * 6 + duration) * 4;
    return { p: cells[k] / 1000, from: cells[k + 1] / 1000, to: cells[k + 2] / 1000, n: cells[k + 3] };
  };
  const bands = [1, 2, 3, 4, 5].map((band) => ({ band, cell: at(band), label: durations[band - 1] }));
  const height = TOP + PLOT + BOTTOM;
  const column = (WIDTH - LEFT - RIGHT) / bands.length;
  const x = (i: number) => LEFT + column * (i + 0.5);
  const y = (v: number) => TOP + ((HIGH - Math.min(HIGH, Math.max(LOW, v))) / (HIGH - LOW)) * PLOT;
  const color = (c: Cell) => (c.n === 0 ? "var(--faint)" : c.from > 0.5 ? "var(--green)" : c.to < 0.5 ? "var(--red)" : "var(--amber)");

  return (
    <figure className="flex flex-col gap-3">
      <div className="flex flex-wrap gap-3 text-sm">
        <label className="flex items-center gap-2">
          <span className="text-faint">Mapa</span>
          <select className="field" value={map} onChange={(e) => setMap(Number(e.target.value))}>
            {maps.map((m) => (
              <option key={m.index} value={m.index}>
                {m.label}
              </option>
            ))}
          </select>
        </label>
        <label className="flex items-center gap-2">
          <span className="text-faint">Elo</span>
          <select className="field" value={elo} onChange={(e) => setElo(Number(e.target.value))}>
            {elos.map((m) => (
              <option key={m.index} value={m.index}>
                {m.label}
              </option>
            ))}
          </select>
        </label>
      </div>
      <div className="overflow-x-auto rounded-md border border-line bg-panel">
        <svg
          viewBox={`0 0 ${WIDTH} ${height}`}
          className="w-full min-w-[34rem]"
          role="img"
          aria-label={`Chance de ${civName} contra ${rivalName} según la duración de la partida`}
        >
          {[0.3, 0.4, 0.5, 0.6, 0.7].map((v) => (
            <g key={v}>
              <line
                x1={LEFT}
                x2={WIDTH - RIGHT}
                y1={y(v)}
                y2={y(v)}
                stroke="var(--line)"
                strokeDasharray={v === 0.5 ? "4 3" : undefined}
                opacity={v === 0.5 ? 1 : 0.5}
              />
              <text x={LEFT - 6} y={y(v) + 4} fontSize={11} fill="var(--faint)" textAnchor="end">
                {percent(v)} %
              </text>
            </g>
          ))}
          {bands.map(({ band, cell, label }, i) => (
            <g key={band}>
              <rect x={x(i) - 9} y={y(cell.to)} width={18} height={Math.max(2, y(cell.from) - y(cell.to))} rx={3} fill={color(cell)} opacity={0.35} />
              <circle cx={x(i)} cy={y(cell.p)} r={5} fill={color(cell)} />
              <text x={x(i)} y={TOP + PLOT + 18} fontSize={11} fill="var(--muted)" textAnchor="middle">
                {label}
              </text>
              <text x={x(i)} y={TOP + PLOT + 34} fontSize={11} fill="var(--faint)" textAnchor="middle">
                {cell.n > 0 ? `${cell.n.toLocaleString("es-AR")} partidas` : "sin partidas"}
              </text>
            </g>
          ))}
        </svg>
      </div>
      <figcaption className="flex flex-col gap-1 text-sm text-muted">
        {bands.map(({ band, cell, label }) => (
          <span key={band}>
            {label}: {civName} ganan {chanceText(cell)}
            {isEven(cell) ? " (parejo)" : ""}
            {cell.n > 0 ? `, ${cell.n.toLocaleString("es-AR")} partidas` : ", estimado (sin partidas)"}
          </span>
        ))}
      </figcaption>
    </figure>
  );
}
