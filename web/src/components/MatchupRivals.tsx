"use client";

import Link from "next/link";
import { useState } from "react";
import { chanceColor, chanceText, isEven, type Cell } from "@/lib/matchups";

/** Un rival con sus 30 casilleros (mapa · 6 + franja de Elo) desde la civ de la página. */
export interface RivalOption {
  rival: string;
  name: string;
  slug: string;
  cells: Cell[];
}

export interface Choice {
  index: number;
  label: string;
}

/** Los rivales de una civ ordenados por la chance, con el mapa y la franja de Elo para elegir (D10, spec 5.5). */
export function MatchupRivals({ civName, rivals, maps, elos }: { civName: string; rivals: RivalOption[]; maps: Choice[]; elos: Choice[] }) {
  const [map, setMap] = useState(0);
  const [elo, setElo] = useState(0);
  const i = map * 6 + elo;
  const rows = [...rivals].sort((x, y) => y.cells[i].p - x.cells[i].p);
  return (
    <div className="flex flex-col gap-3">
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
      <div className="overflow-x-auto rounded-md border border-line">
        <table className="w-full min-w-[30rem] text-sm">
          <thead className="bg-panel-2 text-left text-faint">
            <tr>
              <th className="px-3 py-2 font-normal">Rival</th>
              <th className="px-3 py-2 font-normal">{civName} ganan</th>
              <th className="px-3 py-2 text-right font-normal">Partidas</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => {
              const c = row.cells[i];
              return (
                <tr key={row.rival} className="border-t border-line">
                  <td className="px-3 py-1.5">
                    <Link href={`/cruces/${row.slug}`} className="hover:text-amber">
                      {row.name}
                    </Link>
                  </td>
                  <td className={`px-3 py-1.5 tabular-nums ${chanceColor(c)}`}>
                    {chanceText(c)}
                    {isEven(c) ? <span className="text-faint"> · parejo</span> : null}
                  </td>
                  <td className="px-3 py-1.5 text-right tabular-nums text-muted">
                    {c.n > 0 ? c.n.toLocaleString("es-AR") : "estimado"}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}
