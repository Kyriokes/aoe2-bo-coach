"use client";

import { fill } from "@/i18n";
import { useI18n } from "@/i18n/client";
import { LANG_TAG } from "@/i18n/locales";
import type { GameStart } from "@/lib/coach/types";
import type { MapInfo } from "@/lib/editor/gameData";
import { SOURCES, type Source } from "@/lib/editor/model";
import type { Opening, OpeningKind } from "@/lib/editor/opening";
import { sourceName } from "@/lib/stepText";

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
  const { locale, t: dict } = useI18n();
  const t = dict.editor.start;
  const kindLabel: Record<OpeningKind, string> = { houses: t.kindHouses, direct: t.kindDirect, custom: t.kindCustom };
  const number = (n: number) => new Intl.NumberFormat(LANG_TAG[locale]).format(n);
  const working = Object.entries(start.villagers).filter(([, n]) => (n ?? 0) > 0);
  const sources = (map ? map.sources : [...SOURCES]).filter((s) => s !== "farm");
  const kinds: OpeningKind[] = start.houses ? ["houses", "direct", "custom"] : ["direct", "custom"];
  return (
    <section aria-label={t.label} className="flex flex-col gap-2 rounded-md border border-line bg-panel p-3 text-sm">
      <p>
        <span className="font-semibold text-foreground">{t.title}</span>{" "}
        <span className="text-muted">
          {fill(t.summary, {
            idle: start.idle_villagers,
            working: working.map(([source, n]) => fill(t.working, { n: n ?? 0, source: sourceName(source, locale) })).join(""),
            food: number(start.food),
            wood: number(start.wood),
            gold: number(start.gold),
            stone: number(start.stone),
            pop: start.population,
            cap: start.population_cap,
          })}
          {start.houses ? "" : t.noHouses}
        </span>
      </p>
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-muted">{fill(t.initial, { n: start.idle_villagers })}</span>
        <select
          value={opening.kind}
          onChange={(e) => onOpening({ ...opening, kind: e.target.value as OpeningKind })}
          className="field"
          aria-label={t.kindLabel}
        >
          {kinds.map((kind) => (
            <option key={kind} value={kind}>
              {kindLabel[kind]}
            </option>
          ))}
          {!kinds.includes(opening.kind) ? <option value={opening.kind}>{kindLabel[opening.kind]}</option> : null}
        </select>
        {opening.kind !== "custom" ? (
          <select
            value={opening.source}
            onChange={(e) => onOpening({ ...opening, source: e.target.value as Source })}
            className="field"
            aria-label={t.whereLabel}
          >
            {sources.map((source) => (
              <option key={source} value={source}>
                {sourceName(source, locale)}
              </option>
            ))}
          </select>
        ) : null}
      </div>
      {opening.kind === "custom" ? (
        <p className="text-xs text-faint">
          {t.customNote}
        </p>
      ) : null}
    </section>
  );
}
