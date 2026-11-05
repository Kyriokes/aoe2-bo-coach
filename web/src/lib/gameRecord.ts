import { gunzipSync } from "node:zlib";

/** Lo que acepta la web de un registro de partida (lo mismo que la base y el bucket): hasta 1 MB comprimido. */
export const MAX_RECORD_BYTES = 1024 * 1024;

/**
 * Descomprimido nunca pasa de esto (una partida larga da 2 a 4 MB): lo demás es una bomba de gzip, y descomprimirla
 * entera frena al servidor antes de llegar a la base.
 */
const MAX_UNZIPPED_BYTES = 16 * 1024 * 1024;

/** La línea meta lleva la BO jugada (hasta 128 KB) y poco más: una más larga no se llega a parsear. */
const MAX_META_BYTES = 1024 * 1024;

const INSTALL = /^[0-9a-f]{32}$/;
const SECRET = /^[0-9a-f]{64}$/;

export type RecordIdentity = { install: string; secret: string };

/** La identidad de la instalación que manda la app en sus cabeceras, o null si falta o no tiene el formato. */
export function recordIdentity(headers: Headers): RecordIdentity | null {
  const install = headers.get("x-bocoach-install") ?? "";
  const secret = headers.get("x-bocoach-secret") ?? "";
  return INSTALL.test(install) && SECRET.test(secret) ? { install, secret } : null;
}

/** Lo que Storage acepta borrar en un pedido. Una instalación suma hasta 30 registros por día. */
export const STORAGE_REMOVE_MAX = 1000;

export function inBatches<T>(items: readonly T[], size: number): T[][] {
  const batches: T[][] = [];
  for (let i = 0; i < items.length; i += size) batches.push(items.slice(i, i + size));
  return batches;
}

export function isGameId(value: string | null): value is string {
  return value !== null && INSTALL.test(value);
}

export function isAppVersion(value: string | null): value is string {
  return value !== null && value.length >= 1 && value.length <= 40 && /^[\x20-\x7e]+$/.test(value);
}

/**
 * Valida un registro antes de guardarlo: gzip, tamaño razonable, formato conocido (`meta` con su `schema`) y el mismo
 * identificador de partida que dice la cabecera. Devuelve el motivo si no sirve, o null. Lo recibido se trata con
 * desconfianza igual antes de calibrar nada con esto: acá solo se frena lo que claramente no es un registro.
 */
export function recordProblem(bytes: Uint8Array, gameId: string): string | null {
  if (bytes.length === 0 || bytes.length > MAX_RECORD_BYTES) return "El registro tiene que pesar hasta 1 MB.";
  let unzipped: Buffer;
  try {
    unzipped = gunzipSync(bytes, { maxOutputLength: MAX_UNZIPPED_BYTES });
  } catch {
    return "El registro no es un gzip válido, o descomprimido es demasiado grande.";
  }
  const end = unzipped.indexOf(0x0a);
  const line = end < 0 ? unzipped : unzipped.subarray(0, end);
  if (line.length > MAX_META_BYTES) return "El registro no empieza con su línea meta.";
  let meta: unknown;
  try {
    meta = JSON.parse(line.toString("utf8"));
  } catch {
    return "El registro no empieza con su línea meta.";
  }
  const { t, schema, game_id: game } = (meta ?? {}) as { t?: unknown; schema?: unknown; game_id?: unknown };
  if (t !== "meta") return "El registro no empieza con su línea meta.";
  if (typeof schema !== "number" || !Number.isInteger(schema) || schema < 1) return "El registro no dice su versión.";
  if (game !== gameId) return "El registro es de otra partida.";
  return null;
}
