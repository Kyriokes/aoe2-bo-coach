import { z } from "zod";
import { fill, getDictionary } from "@/i18n";
import { LOCALES, type Locale } from "@/i18n/locales";

/** Versión más nueva del formato de BO que entiende esta web (la misma que la app: BuildOrder.CurrentSchemaVersion). */
export const CURRENT_SCHEMA_VERSION = 2;

/** Tope de count y builders (la población máxima, como BuildOrderValidator.MaxCount en el Core). */
export const MAX_COUNT = 200;

/**
 * Tope del JSON de una BO, en caracteres (la base controla lo mismo). La más grande del repo pesa 12 KB; sin tope, los
 * pasos admiten notas y campos de cualquier largo, y una BO de varios megas la bajaría cada visita a su página y la
 * app de quien la importe.
 */
export const MAX_BODY_CHARS = 128_000;

/** Hasta dónde puede estimarse un clic de edad, en segundos (la base controla lo mismo). */
export const MAX_CLICK_SECONDS = 86_400;

/**
 * Un carácter nulo en el JSON ya escrito: siempre va con su escape, y una barra de verdad va doble. Postgres no guarda ese
 * carácter en un jsonb y sin esto terminaba en un error de la base.
 */
const NULL_CHARACTER = /(?:^|[^\\])(?:\\\\)*\\u0000/;

/** Tope de los after de un paso, de los objetivos del modo asistido y del largo del mapa (el validador del Core y la base controlan lo mismo). */
export const MAX_AFTER = 50;
export const MAX_GOALS = 40;
export const MAX_MAP_CHARS = 40;

type ServerErrors = ReturnType<typeof getDictionary>["serverErrors"];

// count y builders con tope: sin él, un número enorme agota la memoria del Planner (encola una entrada por unidad)
// en el navegador de cualquiera que abra la página de la BO y en la app de quien la importe. Lo mismo after: el análisis
// corre en el hilo de la página.
function stepSchema(t: ServerErrors) {
  return z.looseObject({
    id: z.string().min(1).max(40),
    do: z.enum(["train", "build", "research", "move"]),
    count: z.number().int().min(1).max(MAX_COUNT).optional(),
    builders: z.number().int().min(1).max(MAX_COUNT).nullish(),
    after: z.array(z.string()).max(MAX_AFTER, fill(t.tooManyAfter, { n: MAX_AFTER })).nullish(),
  });
}

/**
 * Lo mínimo que se revisa en el servidor antes de guardar una BO. La validación completa (entidades, edades,
 * dependencias) la hace el Core en el navegador, el mismo validador que la app.
 */
function schemaFor(locale: Locale) {
  const t = getDictionary(locale).serverErrors;
  const step = stepSchema(t);
  return z.looseObject({
    schema_version: z
      .number()
      .int()
      .min(1)
      .max(CURRENT_SCHEMA_VERSION, fill(t.schemaVersion, { n: CURRENT_SCHEMA_VERSION })),
    name: z.string().trim().min(1, t.noName).max(100, fill(t.nameTooLong, { n: 100 })),
    civ: z.string().trim().min(1).max(40).default("any"),
    author: z.string().max(60).nullish(),
    description: z.string().max(4000).nullish(),
    tags: z
      .array(z.string().trim().min(1).max(30, fill(t.tagTooLong, { n: 30 })))
      .max(12, fill(t.tooManyTags, { n: 12 }))
      .default([]),
    steps: z.array(step).min(1, t.noSteps).max(300, fill(t.tooManySteps, { n: 300 })),
    // Las ramas tienen sus propios pasos, con los mismos topes (la base también los controla).
    branches: z
      .array(z.looseObject({ steps: z.array(step).min(1).max(300) }))
      .max(20)
      .nullish(),
    map: z.string().max(MAX_MAP_CHARS, fill(t.mapTooLong, { n: MAX_MAP_CHARS })).nullish(),
    assisted: z
      .looseObject({ goals: z.array(z.unknown()).max(MAX_GOALS, fill(t.tooManyGoals, { n: MAX_GOALS })).nullish() })
      .nullish(),
  });
}

const SCHEMAS = Object.fromEntries(LOCALES.map((locale) => [locale, schemaFor(locale)])) as Record<Locale, ReturnType<typeof schemaFor>>;

export const buildOrderSchema = SCHEMAS.es;

export type BuildOrderBody = z.infer<typeof buildOrderSchema>;

/** Lee y revisa el JSON de una BO. Devuelve el error en el idioma pedido si no sirve. */
export function parseBuildOrder(json: unknown, locale: Locale = "es"): { ok: true; body: BuildOrderBody } | { ok: false; error: string } {
  const t = getDictionary(locale).serverErrors;
  const tooBig = { ok: false as const, error: fill(t.tooBig, { n: MAX_BODY_CHARS / 1000 }) };
  let value = json;
  if (typeof json === "string") {
    if (json.length > MAX_BODY_CHARS) return tooBig;
    try {
      value = JSON.parse(json);
    } catch {
      return { ok: false, error: t.notJson };
    }
  } else if ((JSON.stringify(json) ?? "").length > MAX_BODY_CHARS) {
    return tooBig;
  }
  if (NULL_CHARACTER.test(JSON.stringify(value) ?? "")) return { ok: false, error: t.nullCharacter };
  const result = SCHEMAS[locale].safeParse(value);
  if (!result.success) {
    const issue = result.error.issues[0];
    const where = issue.path.length > 0 ? ` (${issue.path.join(".")})` : "";
    return { ok: false, error: `${issue.message}${where}` };
  }
  return { ok: true, body: result.data };
}

/** Quién ve una BO: todos, o solo su dueño y su app. */
export type Visibility = "public" | "private";

/** Columnas que se guardan junto al JSON para buscar y ordenar. La visibilidad va solo si se pasa. */
export function columnsFor(body: BuildOrderBody, feudalClick?: number | null, castleClick?: number | null, visibility?: Visibility) {
  return {
    ...(visibility ? { visibility } : {}),
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
