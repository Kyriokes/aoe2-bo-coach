import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { beforeAll, describe, expect, it } from "vitest";
import type { CoachRuntime } from "../coach/runtime";
import { coachDir, repoDir, startTestCoach } from "../coach/testing";
import type { Analysis } from "../coach/types";
import { allowedRange, type GameDataLite } from "./constraints";
import { blockOf, fromAnalysis, insertBlock, splitBlock, toJson, type EditorStep } from "./model";

/**
 * El editor contra el Core de verdad (WebAssembly): la ida y vuelta de cada BO del repo por el modelo en unidades,
 * y que `allowedRange` diga lo mismo que el validador en cada movimiento de las BO de ejemplo.
 */
let coach: CoachRuntime;
let data: GameDataLite;

beforeAll(async () => {
  coach = await startTestCoach();
  data = JSON.parse(readFileSync(join(coachDir, "game_data.json"), "utf8")) as GameDataLite;
}, 60_000);

const folders = [join(repoDir, "build_orders"), join(repoDir, "build_orders", "civs"), join(repoDir, "tests", "fixtures", "build_orders")];
const files = folders.flatMap((folder) => readdirSync(folder).filter((f) => f.endsWith(".json")).map((f) => join(folder, f)));

function analyze(json: unknown): Analysis {
  const result = coach.analyze(typeof json === "string" ? json : JSON.stringify(json));
  if (!result.ok) throw new Error(result.error);
  return result;
}

describe("ida y vuelta por el modelo en unidades", () => {
  it(
    "cada BO del repo simula igual después de pasar por el editor",
    () => {
      expect(files.length).toBeGreaterThan(100);
      for (const file of files) {
        const a = analyze(readFileSync(file, "utf8"));
        const state = fromAnalysis(a);
        const b = analyze(toJson(state));
        const name = file.split(/[\\/]/).slice(-2).join("/");

        expect(b.valid, name).toBe(a.valid);
        expect(b.feudal_click, name).toBe(a.feudal_click);
        expect(b.castle_click, name).toBe(a.castle_click);
        expect(b.steps.map((s) => [s.id, s.start, s.end, s.units]), name).toEqual(a.steps.map((s) => [s.id, s.start, s.end, s.units]));
        const texts = (x: Analysis) => x.steps.filter((s, i) => a.steps[i].units > 0).map((s) => s.text);
        expect(texts(b), name).toEqual(texts(a));
        if (a.steps.every((s) => s.units > 0)) expect(b.build_order, name).toEqual(a.build_order);
        expect(b.issues.map((i) => i.message), name).toEqual(a.issues.map((i) => i.message));
      }
    },
    120_000,
  );
});

/** Los problemas que dependen del orden: así se compara "antes" y "después" de un movimiento. */
const ORDER_ISSUE = /recién está en|se hace en|hacen falta 2 edificios|tienen que ir seguidas|depende/;
const orderIssues = (a: Analysis) => new Set(a.issues.filter((i) => ORDER_ISSUE.test(i.message)).map((i) => `${i.step_id}: ${i.message}`));

/** Las cajas que las reglas miran: clics de edad, edificios, lo que se hace fuera del centro urbano y los primeros aldeanos. */
function interesting(block: EditorStep[], index: number) {
  return block.some((s) => {
    const entity = s.what ? data.entities[s.what] : undefined;
    return (s.do === "research" && s.what?.endsWith("_age")) || s.do === "build" || (entity?.made_in && entity.made_in !== "town_center") || s.after.length > 0;
  }) || index < 3;
}

describe("allowedRange concuerda con el validador", () => {
  it(
    "en cada movimiento de cada caja de las BO de ejemplo",
    () => {
      const examples = readdirSync(join(repoDir, "build_orders")).filter((f) => f.endsWith(".json"));
      let moves = 0;
      for (const file of examples) {
        const original = analyze(readFileSync(join(repoDir, "build_orders", file), "utf8"));
        const state = fromAnalysis(original);
        const before = orderIssues(original);
        const seen = new Set<number>();
        state.steps.forEach((step, index) => {
          const chain = blockOf(state.steps, step.id)!;
          if (seen.has(chain.from)) return;
          seen.add(chain.from);
          const { others, block } = splitBlock(state.steps, chain.from, chain.to);
          if (!interesting(block, index)) return;
          const range = allowedRange(others, block, data);
          const where = `${file} ${block.map((s) => s.id).join("+")}`;
          expect(range.lo, where).toBeLessThanOrEqual(chain.from);
          expect(range.hi, where).toBeGreaterThanOrEqual(chain.from);
          // Los bordes del rango y algunos lugares más (cada análisis tarda, y los bordes son lo que importa).
          const candidates = new Set([0, range.lo - 1, range.lo, Math.floor((range.lo + range.hi) / 2), range.hi, range.hi + 1, others.length]);
          for (const p of candidates) {
            if (p < 0 || p > others.length || (p > 0 && p < others.length && others[p].withPrevious)) continue;
            const moved = insertBlock(others, block, p);
            const after = analyze(toJson({ bo: state.bo, steps: moved }));
            const fresh = [...orderIssues(after)].filter((i) => !before.has(i));
            const brokenAfter = moved.some((s, i) => s.after.some((dep) => moved.findIndex((o) => o.id === dep) > i));
            const inside = p >= range.lo && p <= range.hi;
            const label = `${where} → ${p} (${range.lo}..${range.hi}): ${fresh.join(" | ")}`;
            if (inside) expect(fresh, label).toEqual([]);
            else expect(fresh.length > 0 || brokenAfter, label).toBe(true);
            moves++;
          }
        });
      }
      expect(moves).toBeGreaterThan(100);
    },
    120_000,
  );
});
