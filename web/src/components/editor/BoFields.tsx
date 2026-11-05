"use client";

import { useMemo } from "react";
import { fill } from "@/i18n";
import { useI18n } from "@/i18n/client";
import { civNames } from "@/lib/civNames";
import type { MapInfo } from "@/lib/editor/gameData";
import type { EditorBo } from "@/lib/editor/model";

/** Los datos de la BO: nombre, civ, mapa, autor, etiquetas y descripción (los largos, los topes de parseBuildOrder). */
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
  const { locale, t: dict } = useI18n();
  const t = dict.editor.fields;
  const civs = useMemo(() => Object.entries(civNames(locale)).sort((a, b) => a[1].localeCompare(b[1], locale)), [locale]);
  const map = maps.find((m) => m.key === bo.map);
  return (
    <div className="flex flex-col gap-2 rounded-md border border-line bg-panel p-3">
      <div className="flex flex-wrap items-center gap-2">
        <input
          value={bo.name}
          maxLength={100}
          onChange={(e) => onField("name", e.target.value)}
          placeholder={t.namePlaceholder}
          aria-label={t.name}
          className="field min-w-56 flex-1 text-base font-semibold"
        />
        <label className="flex items-center gap-1 text-sm text-muted">
          {t.civ}
          <select value={bo.civ} onChange={(e) => onField("civ", e.target.value)} className="field" aria-label={t.civLabel}>
            <option value="any">{dict.civ.anyShort}</option>
            {civs.map(([key, name]) => (
              <option key={key} value={key}>
                {name}
              </option>
            ))}
          </select>
        </label>
        <label className="flex items-center gap-1 text-sm text-muted" title={map?.note ?? t.mapHint}>
          {t.map}
          <select value={bo.map} onChange={(e) => onField("map", e.target.value)} className="field" aria-label={t.map}>
            {maps.map((m) => (
              <option key={m.key} value={m.key}>
                {m.name}
              </option>
            ))}
            {map ? null : <option value={bo.map}>{fill(t.mapUnknown, { map: bo.map })}</option>}
          </select>
        </label>
      </div>
      <div className="flex flex-wrap items-center gap-2">
        <input value={bo.author} maxLength={60} onChange={(e) => onField("author", e.target.value)} placeholder={t.author} aria-label={t.author} className="field min-w-40 flex-1" />
        <input
          defaultValue={bo.tags.join(", ")}
          key={bo.id + bo.tags.join(",")}
          onBlur={(e) => onTags(e.target.value.split(",").map((t) => t.trim()).filter(Boolean))}
          placeholder={t.tagsPlaceholder}
          aria-label={t.tags}
          className="field min-w-56 flex-[2]"
        />
      </div>
      <textarea
        value={bo.description}
        maxLength={4000}
        onChange={(e) => onField("description", e.target.value)}
        placeholder={t.descriptionPlaceholder}
        aria-label={t.description}
        rows={2}
        className="field"
      />
      {map?.note ? <p className="text-xs text-faint">{map.note}</p> : null}
    </div>
  );
}
