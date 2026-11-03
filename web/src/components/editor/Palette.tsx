"use client";

import { useState, type PointerEvent as ReactPointerEvent, type ReactNode } from "react";
import { fill } from "@/i18n";
import { useI18n } from "@/i18n/client";
import type { PaletteItem } from "@/lib/editor/gameData";
import { ageLabel } from "@/lib/editor/text";
import { searchPalette, type PaletteBar } from "@/lib/editor/palette";

/** Los colores del árbol del juego: unidades azules, edificios naranjas, tecnologías verdes. */
const FRAME: Record<PaletteItem["kind"], string> = {
  train: "#2f6fb0",
  build: "#c0602a",
  research: "#3f8f4a",
  move: "#6b6258",
};

const AGE_BADGE: Record<string, string> = { feudal: "II", castle: "III", imperial: "IV" };

type Handlers = {
  onPointerDown: (e: ReactPointerEvent<HTMLElement>, item: PaletteItem) => void;
  onPick: (item: PaletteItem) => void;
  /** Solo tocar para agregar (la hoja del celular): sin arrastre, así el dedo desplaza la paleta. */
  tapOnly?: boolean;
};

function Item({ item, onPointerDown, onPick, tapOnly }: { item: PaletteItem } & Handlers) {
  const { locale, t } = useI18n();
  const age = ["dark", "feudal", "castle", "imperial"].includes(item.age) ? ageLabel(item.age as "dark", locale) : item.age;
  const badge = item.key.startsWith("villagers:") ? "+1" : item.key === "move" ? "⇄" : null;
  return (
    <button
      type="button"
      title={fill(tapOnly ? t.editor.palette.itemTitle : t.editor.palette.itemTitleDrag, { label: item.label, age })}
      aria-label={item.label}
      onPointerDown={tapOnly ? undefined : (e) => onPointerDown(e, item)}
      onClick={tapOnly ? () => onPick(item) : undefined}
      onKeyDown={(e) => {
        if (e.key === "Enter" || e.key === " ") {
          e.preventDefault();
          onPick(item);
        }
      }}
      className="relative block shrink-0 rounded-sm border-2 hover:brightness-125 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-amber"
      style={{ borderColor: FRAME[item.kind], width: 40, height: 40, touchAction: tapOnly ? "manipulation" : "none", cursor: tapOnly ? "pointer" : "grab" }}
    >
      {/* eslint-disable-next-line @next/next/no-img-element -- íconos del juego, ya chicos (64×64) */}
      <img src={`/icons/${item.icon}.png`} alt="" width={36} height={36} draggable={false} />
      {AGE_BADGE[item.age] ? (
        <span className="absolute -right-1 -top-1 rounded-sm bg-background/90 px-0.5 text-[9px] font-semibold leading-none text-foreground">{AGE_BADGE[item.age]}</span>
      ) : null}
      {badge ? <span className="absolute -bottom-1 -right-1 rounded-sm bg-background/90 px-0.5 text-[10px] leading-none text-foreground">{badge}</span> : null}
    </button>
  );
}

function Row({ label, children }: { label: ReactNode; children: ReactNode }) {
  return (
    <div className="flex flex-wrap items-center gap-1">
      {/* En el celular el nombre va arriba de los íconos, así entran más por renglón. */}
      <span className="w-full shrink-0 text-[11px] font-semibold text-faint sm:w-24">{label}</span>
      {children}
    </div>
  );
}

const Separator = () => <span aria-hidden="true" className="mx-1 h-7 w-px bg-line" />;

/**
 * La paleta como en el juego (D8): aldeanos y clics de edad, construir, y producir en cada edificio (una pestaña por
 * edificio). Muestra solo lo que se puede agregar en el lugar elegido (`where`); el buscador encuentra también lo
 * escondido y dice por qué no está. Se arrastra a la línea de tiempo, o un clic (o Enter) lo agrega en ese lugar.
 */
export function Palette({
  bar,
  all,
  hidden,
  where,
  onAtEnd,
  onPointerDown,
  onPick,
  tapOnly,
}: {
  bar: PaletteBar;
  /** Todas las cajas (también las escondidas), para el buscador. */
  all: PaletteItem[];
  /** Por qué no está cada caja escondida. */
  hidden: Map<string, string>;
  /** El paso después del cual se agrega, o null si va al final. */
  where: string | null;
  onAtEnd: () => void;
} & Handlers) {
  const { t: dict } = useI18n();
  const t = dict.editor.palette;
  const [tab, setTab] = useState<string | null>(null);
  const [query, setQuery] = useState("");
  const current = bar.producers.find((p) => p.key === tab) ?? bar.producers[0] ?? null;
  const found = query.trim() ? searchPalette(all, query) : null;
  const foundShown = found?.filter((i) => !hidden.has(i.key)) ?? [];
  const foundHidden = found?.filter((i) => hidden.has(i.key)) ?? [];
  const handlers = { onPointerDown, onPick, tapOnly };

  return (
    <div className="flex flex-col gap-1.5 rounded-md border border-line bg-panel p-2" aria-label={t.label}>
      <Row label={t.villagers}>
        {bar.villagers.map((item) => (
          <Item key={item.key} item={item} {...handlers} />
        ))}
        {bar.ages.length > 0 ? <Separator /> : null}
        {bar.ages.map((item) => (
          <Item key={item.key} item={item} {...handlers} />
        ))}
        <input
          type="search"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter" && foundShown.length > 0) {
              e.preventDefault();
              onPick(foundShown[0]);
              setQuery("");
            } else if (e.key === "Escape") setQuery("");
          }}
          placeholder={t.searchPlaceholder}
          aria-label={t.searchLabel}
          className="field h-8 w-full py-1 sm:ml-auto sm:w-44"
        />
      </Row>
      {found ? (
        <Row label={t.found}>
          {foundShown.map((item) => (
            <Item key={item.key} item={item} {...handlers} />
          ))}
          {foundHidden.length > 0 ? (
            <ul className="ml-2 flex flex-col text-xs text-muted">
              {foundHidden.slice(0, 4).map((item) => (
                <li key={item.key}>
                  {fill(t.notHere, { item: item.label, reason: hidden.get(item.key) ?? "" })}
                </li>
              ))}
            </ul>
          ) : null}
          {found.length === 0 ? <span className="text-xs text-muted">{t.nothingFound}</span> : null}
        </Row>
      ) : null}
      <Row label={t.build}>
        {bar.build.map((item) => (
          <Item key={item.key} item={item} {...handlers} />
        ))}
      </Row>
      {current ? (
        <>
          <Row label={t.produceIn}>
            {bar.producers.map((tab_) => (
              <button
                key={tab_.key}
                type="button"
                title={tab_.label}
                aria-label={fill(dict.editor.palette.produceInName, { name: tab_.label })}
                aria-pressed={tab_.key === current.key}
                onClick={() => setTab(tab_.key)}
                className={`shrink-0 rounded-sm border p-0.5 focus-visible:outline-2 focus-visible:outline-amber ${tab_.key === current.key ? "border-amber bg-panel-2" : "border-transparent opacity-70 hover:opacity-100"}`}
              >
                {/* eslint-disable-next-line @next/next/no-img-element -- íconos del juego, ya chicos (64×64) */}
                <img src={`/icons/${tab_.key}.png`} alt="" width={26} height={26} draggable={false} />
              </button>
            ))}
          </Row>
          <Row label={current.label}>
            {current.items.map((item) => (
              <Item key={item.key} item={item} {...handlers} />
            ))}
          </Row>
        </>
      ) : (
        <Row label={t.produceIn}>
          <span className="text-xs text-muted">{t.nothingYet}</span>
        </Row>
      )}
      <p className="text-xs text-faint">
        {where ? fill(t.addedAfter, { where }) : t.addedAtEnd}
        {where ? (
          <>
            {" "}
            <button type="button" className="text-amber hover:underline" onClick={onAtEnd}>
              {t.addAtEnd}
            </button>
          </>
        ) : null}
      </p>
    </div>
  );
}
