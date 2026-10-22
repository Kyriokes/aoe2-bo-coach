"use client";

import type { PointerEvent as ReactPointerEvent } from "react";
import type { PaletteGroup, PaletteItem } from "@/lib/editor/gameData";
import { ACTION_COLORS } from "./StepBox";

/**
 * La paleta de pasos: cajas para arrastrar a la línea de tiempo. Un clic (o Enter) las inserta después del paso
 * seleccionado, sin arrastrar.
 */
export function Palette({ groups, onPointerDown, onPick }: { groups: PaletteGroup[]; onPointerDown: (e: ReactPointerEvent<HTMLElement>, item: PaletteItem) => void; onPick: (item: PaletteItem) => void }) {
  return (
    <div className="flex flex-col gap-3 rounded-md border border-line bg-panel p-3" aria-label="Paleta de pasos">
      {groups.map((group) =>
        group.items.length === 0 ? null : (
          <section key={group.title} className="flex flex-col gap-1.5">
            <h3 className="text-xs font-semibold uppercase tracking-wide text-faint">{group.title}</h3>
            <div className="flex flex-wrap gap-1.5">
              {group.items.map((item) => (
                <button
                  key={item.key}
                  type="button"
                  title={`${item.label}: arrastrá a la línea de tiempo, o tocá para agregar después del paso elegido`}
                  onPointerDown={(e) => onPointerDown(e, item)}
                  onKeyDown={(e) => {
                    if (e.key === "Enter" || e.key === " ") {
                      e.preventDefault();
                      onPick(item);
                    }
                  }}
                  className="rounded-md px-2 py-1 text-xs font-medium text-background hover:opacity-90 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-amber"
                  style={{ background: ACTION_COLORS[item.kind], touchAction: "none", cursor: "grab" }}
                >
                  {item.label}
                </button>
              ))}
            </div>
          </section>
        ),
      )}
    </div>
  );
}
