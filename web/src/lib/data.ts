import "server-only";
import { cacheLife, cacheTag } from "next/cache";
import { publicClient } from "./supabase/server";

/** Lo que se muestra de una BO en la biblioteca. */
export interface BuildOrderSummary {
  id: string;
  code: string;
  name: string;
  civ: string;
  tags: string[];
  description: string | null;
  feudal_click: number | null;
  castle_click: number | null;
  votes: number;
  created_at: string;
  author: string;
}

export interface BuildOrderDetail extends BuildOrderSummary {
  owner_id: string;
  updated_at: string;
  body: Record<string, unknown>;
}

export type Sort = "votos" | "recientes";

export interface LibraryQuery {
  q?: string;
  civ?: string;
  tag?: string;
  sort?: Sort;
}

/** Etiqueta del caché de la biblioteca: publicar, editar, borrar o votar la invalida. */
export const LIBRARY_TAG = "biblioteca";
export const buildOrderTag = (code: string) => `bo-${code}`;

const summaryColumns = "id, code, name, civ, tags, description, feudal_click, castle_click, votes, created_at, profiles(display_name)";

interface Row extends Omit<BuildOrderSummary, "author"> {
  profiles: { display_name: string } | null;
}

function toSummary({ profiles, ...row }: Row): BuildOrderSummary {
  return { ...row, author: profiles?.display_name ?? "Anónimo" };
}

/** Saca lo que rompería el filtro de PostgREST (comas, paréntesis, comodines): se busca por palabras. */
function cleanSearch(text: string) {
  return text.replace(/[^\p{L}\p{N} _-]/gu, " ").replace(/\s+/g, " ").trim().slice(0, 60);
}

export async function listBuildOrders(query: LibraryQuery): Promise<BuildOrderSummary[]> {
  "use cache";
  cacheLife("minutes");
  cacheTag(LIBRARY_TAG);

  const db = publicClient();
  if (!db) return [];
  let request = db.from("build_orders").select(summaryColumns).limit(60);
  const search = cleanSearch(query.q ?? "");
  if (search) request = request.or(`name.ilike.*${search}*,description.ilike.*${search}*`);
  if (query.civ) request = request.eq("civ", query.civ);
  if (query.tag) request = request.contains("tags", [query.tag]);
  request =
    query.sort === "recientes"
      ? request.order("created_at", { ascending: false })
      : request.order("votes", { ascending: false }).order("created_at", { ascending: false });

  const { data, error } = await request;
  if (error) throw new Error(`No se pudo leer la biblioteca: ${error.message}`);
  return (data as unknown as Row[]).map(toSummary);
}

/** Civs y tags que hay en la biblioteca, para los filtros. */
export async function listFacets(): Promise<{ civs: string[]; tags: string[] }> {
  "use cache";
  cacheLife("minutes");
  cacheTag(LIBRARY_TAG);

  const db = publicClient();
  if (!db) return { civs: [], tags: [] };
  const { data, error } = await db.from("build_orders").select("civ, tags").limit(2000);
  if (error) throw new Error(`No se pudieron leer los filtros: ${error.message}`);
  const rows = data as { civ: string; tags: string[] }[];
  const sorted = (values: Iterable<string>) => [...new Set(values)].sort((a, b) => a.localeCompare(b, "es"));
  return { civs: sorted(rows.map((r) => r.civ)), tags: sorted(rows.flatMap((r) => r.tags)) };
}

/** Código y última modificación de cada BO, para el sitemap. */
export async function listSitemapEntries(): Promise<{ code: string; updated_at: string }[]> {
  "use cache";
  cacheLife("hours");
  cacheTag(LIBRARY_TAG);

  const db = publicClient();
  if (!db) return [];
  const { data, error } = await db.from("build_orders").select("code, updated_at").order("updated_at", { ascending: false }).limit(5000);
  if (error) throw new Error(`No se pudo armar el sitemap: ${error.message}`);
  return data as { code: string; updated_at: string }[];
}

export async function getBuildOrder(code: string): Promise<BuildOrderDetail | null> {
  "use cache";
  cacheLife("minutes");
  cacheTag(buildOrderTag(code));

  const db = publicClient();
  if (!db || !/^[a-z2-9]{6}$/.test(code)) return null;
  const { data, error } = await db
    .from("build_orders")
    .select(`${summaryColumns}, owner_id, updated_at, body`)
    .eq("code", code)
    .maybeSingle();
  if (error) throw new Error(`No se pudo leer la BO: ${error.message}`);
  if (!data) return null;
  const row = data as unknown as Row & { owner_id: string; updated_at: string; body: Record<string, unknown> };
  return { ...toSummary(row), owner_id: row.owner_id, updated_at: row.updated_at, body: row.body };
}
