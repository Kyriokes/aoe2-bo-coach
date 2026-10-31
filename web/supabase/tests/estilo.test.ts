import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { beforeAll, describe, expect, it } from "vitest";
import type { CoachRuntime } from "../../src/lib/coach/runtime";
import { repoDir, startTestCoach } from "../../src/lib/coach/testing";
import type { Analysis } from "../../src/lib/coach/types";
import { database, type Db } from "./database";

/**
 * La columna style de build_orders la calcula la base con build_order_style, una copia en SQL de BuildOrderStyle
 * del Core. Este test las ata: con todas las BO del repo tienen que dar lo mismo.
 */
let db: Db;
let coach: CoachRuntime;

beforeAll(async () => {
  [db, coach] = await Promise.all([database(), startTestCoach()]);
}, 60_000);

describe("el estilo de la base y el del Core", () => {
  const folders = [join(repoDir, "build_orders"), join(repoDir, "build_orders", "civs")];
  const files = folders.flatMap((dir) => readdirSync(dir).filter((f) => f.endsWith(".json")).map((f) => join(dir, f)));

  it("dan lo mismo con cada BO del repo", async () => {
    expect(files.length).toBeGreaterThan(100);
    const styles = new Set<string | null>();
    for (const file of files) {
      const json = readFileSync(file, "utf8");
      const core = (coach.analyze(json) as Analysis).style;
      const { rows } = await db.query<{ style: string | null }>("select public.build_order_style($1) as style", [JSON.parse(json)]);
      expect(rows[0].style, file).toBe(core);
      styles.add(core);
    }
    // Que la comparación no sea trivial: el repo tiene varios estilos.
    expect(styles.size).toBeGreaterThan(2);
  });
});
