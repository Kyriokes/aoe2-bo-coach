import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { beforeAll, describe, expect, it } from "vitest";
import { parseBuildOrder } from "../../src/lib/buildOrder";
import { database, type Db } from "./database";

/** Los topes de la web y de la base no pueden dejar afuera a una BO real: todas las del repo entran en los dos. */
const repo = join(__dirname, "..", "..", "..");
const folders = ["build_orders", join("build_orders", "civs"), join("tests", "fixtures", "build_orders")];
const files = folders.flatMap((dir) => readdirSync(join(repo, dir)).filter((f) => f.endsWith(".json")).map((f) => join(repo, dir, f)));

let db: Db;

beforeAll(async () => {
  db = await database();
}, 60_000);

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
