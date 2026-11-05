import { beforeEach, describe, expect, it, vi } from "vitest";

/**
 * data.ts con un cliente de Supabase falso que hace lo que hace PostgREST: nunca devuelve más de 1000 filas por pedido
 * (max_rows, supabase/config.toml), aunque le pidan más con limit o con range.
 */
const MAX_ROWS = 1000;

type Row = Record<string, unknown>;

let rows: Row[] = [];
let failFrom = Infinity;
const orders: string[] = [];
const filters: unknown[][] = [];

function query() {
  let from = 0;
  let to = Infinity;
  const q = {
    select: () => q,
    order: (column: string) => {
      orders.push(column);
      return q;
    },
    limit: (n: number) => {
      to = n - 1;
      return q;
    },
    range: (start: number, end: number) => {
      from = start;
      to = end;
      return q;
    },
    eq: (...args: unknown[]) => {
      filters.push(["eq", ...args]);
      return q;
    },
    contains: (...args: unknown[]) => {
      filters.push(["contains", ...args]);
      return q;
    },
    or: () => q,
    then: (resolve: (value: unknown) => unknown) =>
      resolve(
        from >= failFrom
          ? { data: null, error: { message: "se cortó" } }
          : { data: rows.slice(from, Math.min(to + 1, from + MAX_ROWS)), error: null },
      ),
  };
  return q;
}

vi.mock("next/cache", () => ({ cacheLife: () => {}, cacheTag: () => {} }));
vi.mock("./supabase/server", () => ({ publicClient: () => ({ from: () => query() }) }));

beforeEach(() => {
  rows = [];
  failFrom = Infinity;
  orders.length = 0;
  filters.length = 0;
});

describe("leer más de lo que PostgREST da de una vez", () => {
  it("los filtros ven también lo que está después de las primeras 1000 BO", async () => {
    const { listFacets } = await import("./data");
    rows = Array.from({ length: 2500 }, (_, i) => ({ civ: i === 2400 ? "franks" : "britons", tags: i === 1500 ? ["boom"] : ["feudal"] }));

    expect(await listFacets()).toEqual({ civs: ["britons", "franks"], tags: ["boom", "feudal"] });
  });

  it("el sitemap tiene todas las BO", async () => {
    const { listSitemapEntries } = await import("./data");
    rows = Array.from({ length: 2500 }, (_, i) => ({ code: `c${i}`, updated_at: "2026-11-05" }));

    const entries = await listSitemapEntries();
    expect(entries).toHaveLength(2500);
    expect(new Set(entries.map((e) => e.code)).size).toBe(2500);
  });

  it("con un múltiplo justo de la página termina igual", async () => {
    const { listSitemapEntries } = await import("./data");
    rows = Array.from({ length: 2000 }, (_, i) => ({ code: `c${i}`, updated_at: "2026-11-05" }));
    expect(await listSitemapEntries()).toHaveLength(2000);
  });

  it("cada lectura va ordenada por algo que no se repite: si no, una fila podría saltearse entre páginas", async () => {
    const { listFacets, listSitemapEntries } = await import("./data");
    rows = [{ civ: "britons", tags: [], code: "abc234", updated_at: "2026-11-05" }];

    await listFacets();
    expect(orders).toEqual(["code"]);
    orders.length = 0;
    await listSitemapEntries();
    expect(orders).toEqual(["updated_at", "code"]);
  });

  it("si falla una página, el error se ve", async () => {
    const { listFacets, listSitemapEntries } = await import("./data");
    rows = Array.from({ length: 1500 }, () => ({ civ: "britons", tags: [], code: "x", updated_at: "2026-11-05" }));
    failFrom = 1000;

    await expect(listFacets()).rejects.toThrow(/filtros.*se cortó/);
    await expect(listSitemapEntries()).rejects.toThrow(/sitemap.*se cortó/);
  });
});
