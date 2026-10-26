import { describe, expect, it } from "vitest";
import { durationLabel, MAP_NAMES, oneVsOne, percent, type MatchupData } from "./matchups";
import { durationText, mapLine, matchupSitemapPaths, matchupStaticParams, pairCells, pairSummary, rivalsOf, selectionPlace, teamOf } from "./matchupView";
import { buildMatchups, parseTest, same, type CellFn } from "./matchupsTesting";

const tres = (fn: CellFn) => ({ "britons|franks": fn, "britons|mayan": fn, "franks|mayan": fn });

/** La frase de duración del cruce desde `a` con el mapa y la franja de Elo elegidos, como la arma el gráfico. */
const frase = (data: MatchupData, a: string, b: string, civName: string, map = 0, elo = 0) =>
  durationText(
    (band) => oneVsOne(data, a, b, map, elo, band),
    civName,
    (band) => durationLabel(data, band),
    selectionPlace(map > 0 ? MAP_NAMES[data.mapKeys[map - 1]] : null, elo),
  );

describe("lo que muestra cada página de cruces", () => {
  it("rivales con sus 30 casilleros", () => {
    const data = parseTest(buildMatchups(tres((m, e) => [100 * m + 10 * e, 0, 999, 5]), { franks: same(510, 500, 520, 900) }));
    const rivales = rivalsOf(data, "franks");

    expect(rivales.map((r) => r.rival).sort()).toEqual(["britons", "mayan"]);
    const britanos = rivales.find((r) => r.rival === "britons")!;
    expect(britanos.cells).toHaveLength(30);
    // franks va segunda en "britons|franks": desde los francos, la chance se invierte.
    expect(britanos.cells[2 * 6 + 3].p).toBeCloseTo(1 - 0.23, 6);
    const mayas = rivales.find((r) => r.rival === "mayan")!;
    expect(mayas.cells[2 * 6 + 3].p).toBeCloseTo(0.23, 6);
    expect(teamOf(data, "franks")).toHaveLength(30);
    expect(teamOf(data, "spartans")).toBeNull();
    expect(rivalsOf(data, "spartans")).toEqual([]);
  });

  it("el resumen del cruce desde a", () => {
    const data = parseTest(buildMatchups(tres((m) => [m === 0 ? 470 : 400 + 10 * m, 430, 490, m === 4 ? 0 : 80]), {}));
    const resumen = pairSummary(data, "britons", "franks");

    expect(resumen.general.p).toBeCloseTo(0.47, 6);
    expect(resumen.maps.map((m) => m.name)).toEqual(["Todos los mapas", "Arabia", "Arena", "Bosque Negro", "Nómada"]);
    expect(resumen.maps[1].cell.p).toBeCloseTo(0.41, 6);
    expect(resumen.maps[4].cell.n).toBe(0);
    expect(resumen.estimated).toBe(false);
    expect(pairCells(data, "franks", "britons")).toHaveLength(720);
    expect(pairCells(data, "franks", "britons")[0]).toBe(530); // desde los francos: 1000 - 470
  });

  it("los dos lados del cruce suman 100", () => {
    // 50,5 % redondea a 51 y 49,5 % a 50: redondeados por separado daban 101.
    const data = parseTest(buildMatchups({ "aztecs|portuguese": same(505, 480, 530, 900) }, {}));
    const resumen = pairSummary(data, "aztecs", "portuguese");
    expect(percent(resumen.general.p)).toBe(51);
    expect(resumen.rivalPercent).toBe(49);
  });

  it("frase de duración con la regla de honestidad", () => {
    const porDuracion = (franjas: Record<number, [number, number, number, number]>) =>
      parseTest(buildMatchups({ "britons|franks": (_m, _e, d) => franjas[d] ?? (d === 0 ? [480, 460, 500, 300] : [480, 470, 490, 0]) }, {}));

    const cortas = porDuracion({ 1: [540, 491, 587, 50], 5: [420, 380, 460, 40] });
    expect(frase(cortas, "britons", "franks", "Britanos")).toBe(
      "Britanos ganan más en partidas cortas: 54 % en menos de 20 min, 42 % en 55 min o más.",
    );
    expect(frase(cortas, "franks", "britons", "Francos")).toBe(
      "Francos ganan más en partidas largas: 46 % en menos de 20 min, 58 % en 55 min o más.",
    );
    const pareja = porDuracion({ 1: [540, 491, 587, 50], 5: [450, 400, 495, 40] });
    expect(frase(pareja, "britons", "franks", "Britanos")).toBe("Sin diferencia clara por duración.");
    // Una sola franja con partidas: no hay con qué comparar.
    expect(frase(porDuracion({ 3: [480, 440, 520, 60] }), "britons", "franks", "Britanos")).toBeNull();
    expect(frase(porDuracion({}), "britons", "franks", "Britanos")).toBe("Estimado: no hay partidas de este cruce.");
  });

  it("la frase de duración sigue al mapa y a la franja de Elo elegidos", () => {
    // Tendencia en todos los mapas; en Nómada (mapa 4) ninguna partida.
    const data = parseTest(
      buildMatchups(
        {
          "britons|franks": (m, _e, d) =>
            m === 4 ? [480, 400, 560, 0] : d === 1 ? [540, 491, 587, 50] : d === 5 ? [420, 380, 460, 40] : [480, 460, 500, 300],
        },
        {},
      ),
    );
    expect(frase(data, "britons", "franks", "Britanos")).toMatch(/^Britanos ganan más en partidas cortas/);
    expect(frase(data, "britons", "franks", "Britanos", 4)).toBe("Estimado: no hay partidas de este cruce en Nómada.");
    expect(frase(data, "britons", "franks", "Britanos", 4, 2)).toBe(
      "Estimado: no hay partidas de este cruce en Nómada en esa franja de Elo.",
    );
    expect(selectionPlace(null, 0)).toBeNull();
    expect(selectionPlace(null, 3)).toBe("esa franja de Elo");
  });

  it("las entradas del sitemap", () => {
    const data = parseTest(buildMatchups(tres(same(500, 480, 520, 10)), { franks: same(510, 500, 520, 900) }));
    expect(matchupSitemapPaths(data)).toEqual([
      "/cruces",
      "/cruces/britons",
      "/cruces/franks",
      "/cruces/mayan",
      "/cruces/britons-vs-franks",
      "/cruces/britons-vs-mayan",
      "/cruces/franks-vs-mayan",
    ]);
  });

  it("las páginas que se arman al compilar", () => {
    const data = parseTest(buildMatchups(tres(same(500, 480, 520, 10)), { franks: same(510, 500, 520, 900) }));
    expect(matchupStaticParams(data).map((p) => p.slug)).toEqual([
      "britons",
      "franks",
      "mayan",
      "britons-vs-franks",
      "britons-vs-mayan",
      "franks-vs-mayan",
    ]);
    // Sin datos legibles, una página igual: con Cache Components una lista vacía rompe el build, y la página ya dice
    // que no hay estadísticas.
    expect(matchupStaticParams(null)).toEqual([{ slug: "franks" }]);
  });

  it("textos de un mapa sin partidas", () => {
    expect(mapLine({ p: 0.47, from: 0.4, to: 0.54, n: 0 }, "Nómada")).toBe("Estimado: no hay partidas de este cruce en Nómada.");
    expect(mapLine({ p: 0.47, from: 0.4, to: 0.54, n: 0 }, null)).toBe("Estimado: no hay partidas de este cruce.");
    expect(mapLine({ p: 0.47, from: 0.4, to: 0.54, n: 12 }, "Nómada")).toBeNull();
  });
});
