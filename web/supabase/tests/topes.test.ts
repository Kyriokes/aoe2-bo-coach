import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { beforeAll, describe, expect, it } from "vitest";
import { parseBuildOrder } from "../../src/lib/buildOrder";
import { ana, as, database, type Db, insertBo, scouts, signUp } from "./database";

/** Los topes de la web y de la base no pueden dejar afuera a una BO real: todas las del repo entran en los dos. */
const repo = join(__dirname, "..", "..", "..");
const folders = ["build_orders", join("build_orders", "civs"), join("tests", "fixtures", "build_orders")];
const files = folders.flatMap((dir) => readdirSync(join(repo, dir)).filter((f) => f.endsWith(".json")).map((f) => join(repo, dir, f)));

let db: Db;

beforeAll(async () => {
  db = await database();
}, 60_000);

describe("la migración de la revisión", () => {
  const revision = readFileSync(join(__dirname, "..", "migrations", "20261105000000_revision.sql"), "utf8");

  it("se puede correr de nuevo, sin tocar las fechas y pasando a minúsculas lo que ya estaba", async () => {
    const again = await database({ storage: true });
    await signUp(again, ana, "Ana");
    const { rows } = await as<{ code: string }>(again, ana, insertBo, ["Vieja", { ...scouts, civ: "Franks", tags: ["Feudal"] }]);
    const heavy = (await as<{ code: string }>(again, ana, insertBo, ["Pesada", { ...scouts, civ: "Franks" }])).rows[0];
    // Como quedaron antes de esta revisión: la civ tal cual venía, y otra BO que no entra en los topes de ahora.
    await again.exec(`alter table public.build_orders disable trigger build_orders_columns;
      alter table public.build_orders drop constraint build_orders_body_ok;
      update public.build_orders set civ = 'Franks', tags = '{Feudal}', updated_at = '2026-01-01T00:00:00Z';
      update public.build_orders set body = body || jsonb_build_object('pad', repeat('x', 200000)) where code = '${heavy.code}';
      alter table public.build_orders add constraint build_orders_body_ok check (public.build_order_body_ok(body)) not valid;
      alter table public.build_orders enable trigger build_orders_columns;`);

    await again.exec(revision);

    const row = async (code: string) =>
      (await again.query<{ civ: string; tags: string[]; updated_at: Date }>("select civ, tags, updated_at from public.build_orders where code = $1", [code])).rows[0];
    expect(await row(rows[0].code)).toEqual({ civ: "franks", tags: ["feudal"], updated_at: new Date("2026-01-01T00:00:00Z") });
    expect((await row(heavy.code)).civ).toBe("Franks"); // queda como está: se corrige cuando la editen
  }, 60_000);
});

describe("las BO del repo", () => {
  it("entran en los topes de la web y de la base", async () => {
    expect(files.length).toBeGreaterThan(100);
    for (const file of files) {
      const text = readFileSync(file, "utf8");
      const web = parseBuildOrder(text);
      expect(web.ok ? null : web.error, file).toBeNull();
      const { rows } = await db.query<{ ok: boolean }>("select public.build_order_body_ok($1) as ok", [JSON.parse(text)]);
      expect(rows[0].ok, file).toBe(true);
    }
  });
});
