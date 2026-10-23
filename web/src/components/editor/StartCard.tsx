"use client";

import type { GameStart } from "@/lib/coach/types";
import type { MapInfo } from "@/lib/editor/gameData";
import { SOURCES, type Source } from "@/lib/editor/model";
import type { Opening, OpeningKind } from "@/lib/editor/opening";
import { sourceName } from "@/lib/stepText";

const KIND_LABEL: Record<OpeningKind, string> = {
  houses: "Hacen 2 casas y después van a",
  direct: "Van directo a",
  custom: "Los uso yo en los pasos (quedan sin tarea hasta entonces)",
};

const number = (n: number) => new Intl.NumberFormat("es-AR").format(n);

/**
 * La caja "Inicio" (D8): con qué arranca la partida para la civ de la BO (lo que usa el simulador) y qué hacen los
 * aldeanos iniciales, que es el primer paso de la BO.
 */
export function StartCard({
  start,
  opening,
  map,
  onOpening,
}: {
  start: GameStart;
  opening: Opening;
  map: MapInfo | null;
  onOpening: (opening: Opening) => void;
}) {
  const working = Object.entries(start.villagers).filter(([, n]) => (n ?? 0) > 0);
  const sources = (map ? map.sources : [...SOURCES]).filter((s) => s !== "farm");
  const kinds: OpeningKind[] = start.houses ? ["houses", "direct", "custom"] : ["direct", "custom"];
  return (
    <section aria-label="Inicio de la partida" className="flex flex-col gap-2 rounded-md border border-line bg-panel p-3 text-sm">
      <p>
        <span className="font-semibold text-foreground">Inicio (0:00):</span>{" "}
        <span className="text-muted">
          {start.idle_villagers} aldeanos sin tarea
          {working.map(([source, n]) => ` y ${n} en ${sourceName(source)}`).join("")}, un explorador y el centro urbano ·{" "}
          {number(start.food)} de comida · {number(start.wood)} de madera · {number(start.gold)} de oro · {number(start.stone)} de piedra · población{" "}
          {start.population}/{start.population_cap}
          {start.houses ? "" : " (esta civ no hace casas)"}
        </span>
      </p>
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-muted">Los {start.idle_villagers} aldeanos iniciales:</span>
        <select
          value={opening.kind}
          onChange={(e) => onOpening({ ...opening, kind: e.target.value as OpeningKind })}
          className="field"
          aria-label="Qué hacen los aldeanos iniciales"
        >
          {kinds.map((kind) => (
            <option key={kind} value={kind}>
              {KIND_LABEL[kind]}
            </option>
          ))}
          {!kinds.includes(opening.kind) ? <option value={opening.kind}>{KIND_LABEL[opening.kind]}</option> : null}
        </select>
        {opening.kind !== "custom" ? (
          <select
            value={opening.source}
            onChange={(e) => onOpening({ ...opening, source: e.target.value as Source })}
            className="field"
            aria-label="Adónde van los aldeanos iniciales"
          >
            {sources.map((source) => (
              <option key={source} value={source}>
                {sourceName(source)}
              </option>
            ))}
          </select>
        ) : null}
      </div>
      {opening.kind === "custom" ? (
        <p className="text-xs text-faint">
          Los iniciales solo trabajan cuando un paso de construir o de mover los toma &quot;sin tarea&quot; (las cajas nuevas de la paleta los
          toman solas mientras haya).
        </p>
      ) : null}
    </section>
  );
}
