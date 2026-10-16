import { z } from "zod";

/** Versión más nueva del formato de BO que entiende esta web (la misma que la app: BuildOrder.CurrentSchemaVersion). */
export const CURRENT_SCHEMA_VERSION = 1;

const step = z.looseObject({
  id: z.string().min(1).max(40),
  do: z.enum(["train", "build", "research", "move"]),
});

/**
 * Lo mínimo que se revisa en el servidor antes de guardar una BO. La validación completa (entidades, edades,
 * dependencias) la hace el Core en el navegador, el mismo validador que la app.
 */
export const buildOrderSchema = z.looseObject({
  schema_version: z
    .number()
    .int()
    .min(1)
    .max(CURRENT_SCHEMA_VERSION, `Esta web entiende hasta schema_version ${CURRENT_SCHEMA_VERSION}.`),
  name: z.string().trim().min(1, "Falta el nombre.").max(100),
  civ: z.string().trim().min(1).max(40).default("any"),
  author: z.string().max(60).nullish(),
  description: z.string().max(4000).nullish(),
  tags: z.array(z.string().trim().min(1).max(30)).max(12).default([]),
  steps: z.array(step).min(1, "La BO no tiene pasos.").max(300),
});

export type BuildOrderBody = z.infer<typeof buildOrderSchema>;

/** Lee y revisa el JSON de una BO. Devuelve el error en español si no sirve. */
export function parseBuildOrder(json: unknown): { ok: true; body: BuildOrderBody } | { ok: false; error: string } {
  let value = json;
  if (typeof json === "string") {
    try {
      value = JSON.parse(json);
    } catch {
      return { ok: false, error: "No es un JSON válido." };
    }
  }
  const result = buildOrderSchema.safeParse(value);
  if (!result.success) {
    const issue = result.error.issues[0];
    const where = issue.path.length > 0 ? ` (${issue.path.join(".")})` : "";
    return { ok: false, error: `${issue.message}${where}` };
  }
  return { ok: true, body: result.data };
}

/** Columnas que se guardan junto al JSON para buscar y ordenar. */
export function columnsFor(body: BuildOrderBody, feudalClick?: number | null, castleClick?: number | null) {
  return {
    name: body.name,
    civ: body.civ,
    tags: body.tags,
    description: body.description ?? null,
    schema_version: body.schema_version,
    body,
    feudal_click: roundSeconds(feudalClick),
    castle_click: roundSeconds(castleClick),
  };
}

function roundSeconds(value?: number | null) {
  return typeof value === "number" && Number.isFinite(value) && value >= 0 ? Math.round(value) : null;
}

/** "9:05" a partir de segundos (como GameClock.Format de la app). */
export function formatClock(seconds: number) {
  const s = Math.max(0, Math.floor(seconds));
  return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, "0")}`;
}
