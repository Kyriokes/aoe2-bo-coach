"use client";

import { useState, type PointerEvent as ReactPointerEvent, type ReactNode } from "react";
import type { PaletteItem } from "@/lib/editor/gameData";
import { searchPalette, type PaletteBar } from "@/lib/editor/palette";

/** Los colores del árbol del juego: unidades azules, edificios naranjas, tecnologías verdes. */
const FRAME: Record<PaletteItem["kind"], string> = {
  train: "#2f6fb0",
  build: "#c0602a",
  research: "#3f8f4a",
  move: "#6b6258",
};

const AGE_BADGE: Record<string, string> = { feudal: "II", castle: "III", imperial: "IV" };
const AGE_NAME: Record<string, string> = { dark: "Edad Oscura", feudal: "Feudal", castle: "Castillos", imperial: "Imperial" };

type Handlers = { onPointerDown: (e: ReactPointerEvent<HTMLElement>, item: PaletteItem) => void; onPick: (item: PaletteItem) => void };

function Item({ item, onPointerDown, onPick }: { item: PaletteItem } & Handlers) {
  const badge = item.key.startsWith("villagers:") ? "+1" : item.key === "move" ? "⇄" : null;
  return (
    <button
      type="button"
      title={`${item.label} (${AGE_NAME[item.age] ?? item.age}): arrastrá a la línea de tiempo, o tocá para agregar`}
      aria-label={item.label}
      onPointerDown={(e) => onPointerDown(e, item)}
      onKeyDown={(e) => {
        if (e.key === "Enter" || e.key === " ") {
          e.preventDefault();
          onPick(item);
        }
      }}
      className="relative block shrink-0 rounded-sm border-2 hover:brightness-125 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-amber"
      style={{ borderColor: FRAME[item.kind], width: 40, height: 40, touchAction: "none", cursor: "grab" }}
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
      <span className="w-24 shrink-0 text-[11px] font-semibold text-faint">{label}</span>
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
  const [tab, setTab] = useState<string | null>(null);
  const [query, setQuery] = useState("");
  const current = bar.producers.find((t) => t.key === tab) ?? bar.producers[0] ?? null;
  const found = query.trim() ? searchPalette(all, query) : null;
  const foundShown = found?.filter((i) => !hidden.has(i.key)) ?? [];
  const foundHidden = found?.filter((i) => hidden.has(i.key)) ?? [];
  const handlers = { onPointerDown, onPick };

  return (
    <div className="flex flex-col gap-1.5 rounded-md border border-line bg-panel p-2" aria-label="Paleta de pasos">
      <Row label="Aldeanos">
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
          placeholder="Buscar: arquero, telar…"
          aria-label="Buscar en la paleta"
          className="field ml-auto h-8 w-44 py-1"
        />
      </Row>
      {found ? (
        <Row label="Encontrado">
          {foundShown.map((item) => (
            <Item key={item.key} item={item} {...handlers} />
          ))}
          {foundHidden.length > 0 ? (
            <ul className="ml-2 flex flex-col text-xs text-muted">
              {foundHidden.slice(0, 4).map((item) => (
                <li key={item.key}>
                  No se puede acá: {item.label} ({hidden.get(item.key)})
                </li>
              ))}
            </ul>
          ) : null}
          {found.length === 0 ? <span className="text-xs text-muted">No hay nada con ese nombre</span> : null}
        </Row>
      ) : null}
      <Row label="Construir">
        {bar.build.map((item) => (
          <Item key={item.key} item={item} {...handlers} />
        ))}
      </Row>
      {current ? (
        <>
          <Row label="Producir en">
            {bar.producers.map((t) => (
              <button
                key={t.key}
                type="button"
                title={t.label}
                aria-label={`Producir en ${t.label}`}
                aria-pressed={t.key === current.key}
                onClick={() => setTab(t.key)}
                className={`shrink-0 rounded-sm border p-0.5 focus-visible:outline-2 focus-visible:outline-amber ${t.key === current.key ? "border-amber bg-panel-2" : "border-transparent opacity-70 hover:opacity-100"}`}
              >
                {/* eslint-disable-next-line @next/next/no-img-element -- íconos del juego, ya chicos (64×64) */}
                <img src={`/icons/${t.key}.png`} alt="" width={26} height={26} draggable={false} />
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
        <Row label="Producir en">
          <span className="text-xs text-muted">Nada todavía en este lugar: hace falta construir el edificio antes (o subir de edad).</span>
        </Row>
      )}
      <p className="text-xs text-faint">
        Se agrega {where ? <>después de &quot;{where}&quot;</> : "al final"} (solo se ve lo que se puede hacer ahí).
        {where ? (
          <>
            {" "}
            <button type="button" className="text-amber hover:underline" onClick={onAtEnd}>
              Agregar al final
            </button>
          </>
        ) : null}
      </p>
    </div>
  );
}
