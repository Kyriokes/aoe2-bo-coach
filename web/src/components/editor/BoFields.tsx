"use client";

import { CIV_NAMES } from "@/lib/civNames";
import type { MapInfo } from "@/lib/editor/gameData";
import type { EditorBo } from "@/lib/editor/model";

const CIVS = Object.entries(CIV_NAMES).sort((a, b) => a[1].localeCompare(b[1], "es"));

/** Los datos de la BO: nombre, civ, mapa, autor, etiquetas y descripción. */
export function BoFields({
  bo,
  maps,
  onField,
  onTags,
}: {
  bo: EditorBo;
  maps: MapInfo[];
  onField: (field: "name" | "civ" | "map" | "author" | "description", value: string) => void;
  onTags: (tags: string[]) => void;
}) {
  const map = maps.find((m) => m.key === bo.map);
  return (
    <div className="flex flex-col gap-2 rounded-md border border-line bg-panel p-3">
      <div className="flex flex-wrap items-center gap-2">
        <input
          value={bo.name}
          onChange={(e) => onField("name", e.target.value)}
          placeholder="Nombre de la build order"
          aria-label="Nombre"
          className="field min-w-56 flex-1 text-base font-semibold"
        />
        <label className="flex items-center gap-1 text-sm text-muted">
          Civ
          <select value={bo.civ} onChange={(e) => onField("civ", e.target.value)} className="field" aria-label="Civilización">
            <option value="any">Cualquiera</option>
            {CIVS.map(([key, name]) => (
              <option key={key} value={key}>
                {name}
              </option>
            ))}
          </select>
        </label>
        <label className="flex items-center gap-1 text-sm text-muted" title={map?.note ?? "El mapa dice qué fuentes hay y si hay agua"}>
          Mapa
          <select value={bo.map} onChange={(e) => onField("map", e.target.value)} className="field" aria-label="Mapa">
            {maps.map((m) => (
              <option key={m.key} value={m.key}>
                {m.name}
              </option>
            ))}
            {map ? null : <option value={bo.map}>{bo.map} (no está en maps.json)</option>}
          </select>
        </label>
      </div>
      <div className="flex flex-wrap items-center gap-2">
        <input value={bo.author} onChange={(e) => onField("author", e.target.value)} placeholder="Autor" aria-label="Autor" className="field min-w-40 flex-1" />
        <input
          defaultValue={bo.tags.join(", ")}
          key={bo.id + bo.tags.join(",")}
          onBlur={(e) => onTags(e.target.value.split(",").map((t) => t.trim()).filter(Boolean))}
          placeholder="Etiquetas, separadas por coma (arabia, feudal, scouts)"
          aria-label="Etiquetas"
          className="field min-w-56 flex-[2]"
        />
      </div>
      <textarea
        value={bo.description}
        onChange={(e) => onField("description", e.target.value)}
        placeholder="Descripción: para quién es, qué hacer después, de dónde salió"
        aria-label="Descripción"
        rows={2}
        className="field"
      />
      {map?.note ? <p className="text-xs text-faint">{map.note}</p> : null}
    </div>
  );
}
