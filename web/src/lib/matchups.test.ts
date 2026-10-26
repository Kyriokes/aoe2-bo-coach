import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { CIV_NAMES } from "./civNames";
import {
  chanceText,
  durationLabel,
  eloLabel,
  isEven,
  MAP_NAMES,
  oneVsOne,
  pairSlug,
  parseMatchups,
  parseSlug,
  team,
  trend,
  type Cell,
} from "./matchups";
import { buildMatchups, parseTest, same } from "./matchupsTesting";

const build = buildMatchups;
const parse = parseTest;

describe("estadísticas de cruces", () => {
  it("lee la cabecera y las franjas", () => {
    const data = parse(build({ "britons|franks": same(497, 469, 525, 93) }, { franks: same(510, 500, 520, 900) }));
    expect(data.source.name).toBe("aoe2-guide");
    expect(data.source.from).toBe("2026-06-01");
    expect(data.patch.label).toBe("Update 185872");
    expect(data.patch.previousMixed).toBe(false);
    expect(data.matches).toEqual({ oneVsOne: 100, team: 50 });
    expect(data.eloLimits).toEqual([850, 1050, 1300, 1600]);
    expect(data.minuteLimits).toEqual([20, 30, 40, 55]);
    expect(data.mapKeys).toEqual(["arabia", "arena", "black_forest", "nomad"]);
    expect(eloLabel(data, 0)).toBe("todas");
    expect(eloLabel(data, 1)).toBe("menos de 850");
    expect(eloLabel(data, 3)).toBe("de 1050 a 1300");
    expect(eloLabel(data, 5)).toBe("1600 o más");
    expect(durationLabel(data, 0)).toBe("cualquier duración");
    expect(durationLabel(data, 1)).toBe("menos de 20 min");
    expect(durationLabel(data, 4)).toBe("de 40 a 55 min");
    expect(durationLabel(data, 5)).toBe("55 min o más");
  });

  it("cada casillero en su lugar", () => {
    const data = parse(build({ "britons|franks": (m, e, d) => [100 * m + 10 * e + d, 0, 999, 7] }, { franks: (m, e, d) => [100 * m + 10 * e + d, 0, 999, 9] }));
    expect(oneVsOne(data, "britons", "franks", 2, 3, 4)?.p).toBeCloseTo(0.234, 6);
    expect(oneVsOne(data, "britons", "franks", 2, 3, 4)?.n).toBe(7);
    expect(team(data, "franks", 4, 5, 5)?.p).toBeCloseTo(0.455, 6);
  });

  it("al revés invierte la chance y el rango", () => {
    const data = parse(build({ "britons|franks": same(600, 550, 650, 40) }, {}));
    expect(oneVsOne(data, "franks", "britons", 1, 0, 0)).toEqual({ p: 0.4, from: 0.35, to: 0.45, n: 40 });
    expect(oneVsOne(data, "britons", "franks", 1, 0, 0)?.p).toBeCloseTo(0.6, 6);
  });

  it("espejo y civ sin datos dan null", () => {
    const data = parse(build({ "britons|franks": same(600, 550, 650, 40) }, { franks: same(510, 500, 520, 900) }));
    expect(oneVsOne(data, "franks", "franks", 0, 0, 0)).toBeNull();
    expect(oneVsOne(data, "franks", "spartans", 0, 0, 0)).toBeNull();
    expect(team(data, "spartans", 0, 0, 0)).toBeNull();
  });

  const porDuracion = (franjas: Record<number, [number, number, number, number]>) =>
    parse(build({ "britons|franks": (_m, _e, d) => franjas[d] ?? [480, 470, 490, 0] }, {}));

  it("tendencia a partidas cortas", () => {
    const data = porDuracion({ 1: [540, 491, 587, 50], 5: [420, 380, 460, 40] });
    const t = trend((band) => oneVsOne(data, "britons", "franks", 1, 0, band));
    expect(t).not.toBeNull();
    expect(t?.shortBand).toBe(1);
    expect(t?.longBand).toBe(5);
    expect(t?.favorsShort).toBe(true);
    expect(trend((band) => oneVsOne(data, "franks", "britons", 1, 0, band))?.favorsShort).toBe(false);
  });

  it("sin tendencia si los rangos se tocan", () => {
    const data = porDuracion({ 1: [540, 491, 587, 50], 5: [450, 400, 495, 40] });
    expect(trend((band) => oneVsOne(data, "britons", "franks", 1, 0, band))).toBeNull();
  });

  it("sin partidas no hay tendencia", () => {
    expect(trend((band) => oneVsOne(porDuracion({}), "britons", "franks", 1, 0, band))).toBeNull();
    const una = porDuracion({ 3: [540, 491, 587, 50] });
    expect(trend((band) => oneVsOne(una, "britons", "franks", 1, 0, band))).toBeNull();
  });

  it("un JSON roto o de otra versión da null", () => {
    const bien = build({ "britons|franks": same(600, 550, 650, 40) }, {});
    expect(parseMatchups("")).toBeNull();
    expect(parseMatchups("{")).toBeNull();
    expect(parseMatchups("[1, 2]")).toBeNull();
    expect(parseMatchups(bien.replace('"schema_version":1', '"schema_version":2'))).toBeNull();
    const corta = JSON.parse(build({ "britons|franks": same(600, 550, 650, 40), "britons|mayan": same(400, 350, 450, 10) }, {}));
    corta.one_vs_one["britons|mayan"].pop();
    const leida = parse(JSON.stringify(corta));
    expect(oneVsOne(leida, "britons", "franks", 0, 0, 0)).not.toBeNull();
    expect(oneVsOne(leida, "britons", "mayan", 0, 0, 0)).toBeNull();
  });

  it("textos de un casillero", () => {
    const cell: Cell = { p: 0.494, from: 0.46, to: 0.525, n: 93 };
    expect(chanceText(cell)).toBe("49 % (46 a 53)");
    expect(isEven(cell)).toBe(true);
    expect(isEven({ p: 0.45, from: 0.4, to: 0.49, n: 9 })).toBe(false);
  });

  it("el slug ordena y reconoce las civs", () => {
    expect(pairSlug("franks", "britons")).toBe("britons-vs-franks");
    expect(parseSlug("britons-vs-franks")).toEqual({ a: "britons", b: "franks", canonical: true });
    expect(parseSlug("franks-vs-britons")).toEqual({ a: "britons", b: "franks", canonical: false });
    expect(parseSlug("franks")).toEqual({ civ: "franks" });
    expect(parseSlug("spartans")).toEqual({ civ: "spartans" }); // está en CIV_NAMES; el 404 lo da la página sin datos
    expect(parseSlug("zzz-vs-franks")).toBeNull();
    expect(parseSlug("franks-vs-franks")).toBeNull();
    expect(parseSlug("zzz")).toBeNull();
    expect(parseSlug("franks-vs-britons-vs-celts")).toBeNull();
  });

  it("los nombres de mapas son los de maps.json", () => {
    const text = readFileSync(fileURLToPath(new URL("../../../data/maps.json", import.meta.url)), "utf8");
    const maps = JSON.parse(text.split("\n").filter((line) => !line.trimStart().startsWith("//")).join("\n")).maps;
    for (const [key, name] of Object.entries(MAP_NAMES)) expect(maps[key].name).toBe(name);
  });

  it("el archivo real", () => {
    const data = parse(readFileSync(fileURLToPath(new URL("../../../data/matchup_stats.json", import.meta.url)), "utf8"));
    expect(data.oneVsOne.size).toBe(1378);
    const civs = new Set([...data.oneVsOne.keys()].flatMap((key) => key.split("|")));
    expect(civs.size).toBe(53);
    for (const civ of civs) expect(Object.hasOwn(CIV_NAMES, civ)).toBe(true);
    for (const [a, b] of [["britons", "franks"], ["aztecs", "mayan"], ["franks", "mongols"], ["khmer", "wu"], ["celts", "goths"]]) {
      const ida = oneVsOne(data, a, b, 0, 0, 0);
      const vuelta = oneVsOne(data, b, a, 0, 0, 0);
      expect(ida && vuelta).toBeTruthy();
      expect((ida?.p ?? 0) + (vuelta?.p ?? 0)).toBeCloseTo(1, 3);
    }
  });
});
