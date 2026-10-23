"use client";

import type { PointerEvent as ReactPointerEvent } from "react";
import type { PaletteItem, TreeColumn } from "@/lib/editor/gameData";
import { AGE_ORDER } from "@/lib/editor/constraints";

const AGE_ROWS: Record<string, string> = { dark: "I · Oscura", feudal: "II · Feudal", castle: "III · Castillos", imperial: "IV · Imperial" };

/** Los colores del árbol del juego: unidades azules, edificios naranjas, tecnologías verdes. */
const FRAME: Record<PaletteItem["kind"], string> = {
  train: "#2f6fb0",
  build: "#c0602a",
  research: "#3f8f4a",
  move: "#6b6258",
};

function Item({ item, compact, onPointerDown, onPick }: { item: PaletteItem; compact?: boolean; onPointerDown: (e: ReactPointerEvent<HTMLElement>, item: PaletteItem) => void; onPick: (item: PaletteItem) => void }) {
  const size = compact ? 34 : 42;
  return (
    <button
      type="button"
      title={`${item.label}: arrastrá a la línea de tiempo, o tocá para agregar después del paso elegido`}
      aria-label={item.label}
      onPointerDown={(e) => onPointerDown(e, item)}
      onKeyDown={(e) => {
        if (e.key === "Enter" || e.key === " ") {
          e.preventDefault();
          onPick(item);
        }
      }}
      className="group flex w-[4.5rem] flex-col items-center gap-0.5 rounded focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-amber"
      style={{ touchAction: "none", cursor: "grab" }}
    >
      <span className="relative block rounded-sm border-2 group-hover:brightness-125" style={{ borderColor: FRAME[item.kind], width: size + 4, height: size + 4 }}>
        {/* eslint-disable-next-line @next/next/no-img-element -- íconos del juego, ya chicos (64×64) */}
        <img src={`/icons/${item.icon}.png`} alt="" width={size} height={size} draggable={false} />
        {item.key.startsWith("villagers:") || item.key === "move" ? (
          // El aldeano chiquito encima de la fuente (y en mover, unas flechas).
          <span className="absolute -bottom-1 -right-1 rounded-sm bg-background/90 px-0.5 text-[10px] leading-none text-foreground">{item.key === "move" ? "⇄" : "+1"}</span>
        ) : null}
      </span>
      <span className="line-clamp-2 text-center text-[10px] leading-tight text-muted">{item.label}</span>
    </button>
  );
}

/**
 * La paleta como el árbol de tecnologías del juego: una columna por edificio (se construye tocando su ícono de
 * arriba) con lo que produce en la fila de su edad, más "Edades", "Aldeanos" y "Otros edificios". Se arrastra a la
 * línea de tiempo, o un clic (o Enter) lo agrega después del paso elegido.
 */
export function Palette({ columns, onPointerDown, onPick }: { columns: TreeColumn[]; onPointerDown: (e: ReactPointerEvent<HTMLElement>, item: PaletteItem) => void; onPick: (item: PaletteItem) => void }) {
  const ages = AGE_ORDER.filter((age) => columns.some((c) => c.byAge[age]?.length > 0));
  return (
    <div className="overflow-x-auto rounded-md border border-line bg-panel" aria-label="Paleta de pasos">
      <div className="grid w-max gap-x-1" style={{ gridTemplateColumns: `5.5rem repeat(${columns.length}, minmax(4.75rem, auto))` }}>
        <div className="sticky left-0 z-10 bg-panel" />
        {columns.map((column) => (
          <div key={column.key} className="flex flex-col items-center gap-1 border-b border-line px-1 pb-2 pt-2">
            {column.building ? (
              <Item item={column.building} onPointerDown={onPointerDown} onPick={onPick} />
            ) : (
              <span className="flex h-full items-end text-center text-[11px] font-semibold uppercase tracking-wide text-faint">{column.label}</span>
            )}
          </div>
        ))}
        {ages.map((age, row) => (
          <div key={age} className="contents">
            <div className={`sticky left-0 z-10 flex items-start bg-panel px-2 pt-3 text-[11px] font-semibold text-faint ${row % 2 ? "bg-panel" : ""}`}>{AGE_ROWS[age]}</div>
            {columns.map((column) => (
              <div key={column.key} className={`flex flex-col items-center gap-1 px-1 py-2 ${row % 2 ? "bg-white/[0.02]" : ""}`}>
                {(column.byAge[age] ?? []).map((item) => (
                  <Item key={item.key} item={item} compact={column.key === "villagers"} onPointerDown={onPointerDown} onPick={onPick} />
                ))}
              </div>
            ))}
          </div>
        ))}
      </div>
    </div>
  );
}
