import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import type { Analysis } from "../coach/types";
import { paletteItems, parseMaps } from "./gameData";
import { LABEL_W, labelWidthFor, LANE_H, lanesFor, MIN_PX_PER_MIN, NARROW_LABEL_W, ORDINAL_W, PAD_L, placeLanes, scaleFor, timelineWidth, timeSpan, VILLAGERS_LANE } from "./layout";
import type { EditorStep } from "./model";

function step(id: string, extra: Partial<EditorStep> = {}): EditorStep {
  return { id, do: "train", what: "villager", count: 1, assign: "sheep", after: [], withPrevious: false, ...extra };
}

/** Un análisis con los tiempos y bloqueos que se le pasen. */
function analysis(times: Record<string, [number | null, number | null]>, blocks: Analysis["blocks"] = []): Analysis {
  return {
    ok: true,
    valid: true,
    id: "x",
    name: "x",
    civ: "any",
    map: null,
    author: null,
    description: null,
    schema_version: 2,
    tags: [],
    targets: {},
    start: { food: 200, wood: 200, gold: 100, stone: 200, population: 4, population_cap: 5, idle_villagers: 3, villagers: {}, houses: true },
    build_order: {},
    issues: [],
    steps: Object.entries(times).map(([id, [start, end]]) => ({ id, text: `paso ${id}`, do: "train", what: null, count: 1, units: 1, note: null, start, end })),
    blocks,
  };
}

describe("escala", () => {
  it("timeSpan llega un minuto después del último paso o bloqueo, en minutos, y nunca menos de 10", () => {
    expect(timeSpan(null)).toBe(600);
    expect(timeSpan(analysis({ a: [0, 100] }))).toBe(600);
    expect(timeSpan(analysis({ a: [0, 100], b: [800, 1250] }))).toBe(1320);
    expect(timeSpan(analysis({ a: [0, 100] }, [{ step_id: "a", reason: "resources", from: 100, to: 1500, missing: "food" }]))).toBe(1560);
  });

  it("en pantallas angostas la columna de nombres se achica y la línea se desplaza de costado", () => {
    expect(labelWidthFor(1024)).toBe(LABEL_W);
    expect(labelWidthFor(375)).toBe(NARROW_LABEL_W);
    // En PC la BO entera entra en el ancho que hay, aunque sea larga.
    expect(timelineWidth(1016, 600)).toBe(1016);
    expect(timelineWidth(1016, 30 * 60)).toBe(1016);
    // En un celular, 22 minutos no entran a 72 px por minuto: la línea mide más y se desplaza.
    expect(timelineWidth(343, 22 * 60)).toBe(NARROW_LABEL_W + 8 + 22 * MIN_PX_PER_MIN + 8);
    const scale = scaleFor(timelineWidth(343, 22 * 60), 22 * 60, NARROW_LABEL_W);
    expect(scale.padL).toBe(NARROW_LABEL_W + 8);
    expect(scale.x(60) - scale.x(0)).toBeCloseTo(MIN_PX_PER_MIN, 5);
  });

  it("scaleFor pasa de segundos a píxeles y vuelve", () => {
    const scale = scaleFor(1016, 600);
    expect(scale.x(0)).toBe(PAD_L);
    expect(scale.padL).toBe(PAD_L);
    expect(scale.x(600)).toBe(1016 - 8);
    expect(scale.t(scale.x(123))).toBeCloseTo(123);
  });
});

describe("una línea por edificio", () => {
  const data = {
    entities: {
      villager: { kind: "unit" as const, age: "dark" as const, made_in: "town_center" },
      house: { kind: "building" as const, age: "dark" as const },
      farm: { kind: "building" as const, age: "dark" as const },
      barracks: { kind: "building" as const, age: "dark" as const },
      archery_range: { kind: "building" as const, age: "feudal" as const },
      archer: { kind: "unit" as const, age: "feudal" as const, made_in: "archery_range" },
      militia: { kind: "unit" as const, age: "dark" as const, made_in: "barracks" },
      loom: { kind: "tech" as const, age: "dark" as const, made_in: "town_center" },
      town_center: { kind: "building" as const, age: "castle" as const },
    },
  };
  const steps: EditorStep[] = [
    step("h", { do: "build", what: "house", count: 2, builders: 3 }),
    step("v", { count: 3 }),
    step("r", { do: "build", what: "archery_range", count: 2, assign: "wood" }),
    step("l", { do: "research", what: "loom" }),
    step("a", { do: "train", what: "archer", count: 4, assign: undefined }),
    step("m", { do: "move", count: 2, from: "sheep", assign: "wood" }),
  ];
  const a: Analysis = {
    ...analysis({ h: [0, 30], v: [0, 75], r: [80, 200], l: [75, 100], a: [140, 270], m: [90, 90] }),
    buildings: [
      { id: "town_center#1", key: "town_center", step_id: null, built: null },
      { id: "h#1", key: "house", step_id: "h", built: 20 },
      { id: "h#2", key: "house", step_id: "h", built: 30 },
      { id: "r#1", key: "archery_range", step_id: "r", built: 140 },
      { id: "r#2", key: "archery_range", step_id: "r", built: 200 },
    ],
    jobs: [
      { step_id: "h", kind: "build", what: "house", building: "h#1", start: 0, end: 20 },
      { step_id: "h", kind: "build", what: "house", building: "h#2", start: 20, end: 30 },
      { step_id: "v", kind: "produce", what: "villager", building: "town_center#1", start: 0, end: 25 },
      { step_id: "v", kind: "produce", what: "villager", building: "town_center#1", start: 25, end: 50 },
      { step_id: "v", kind: "produce", what: "villager", building: "town_center#1", start: 50, end: 75 },
      { step_id: "l", kind: "produce", what: "loom", building: "town_center#1", start: 75, end: 100 },
      { step_id: "r", kind: "build", what: "archery_range", building: "r#1", start: 80, end: 140 },
      { step_id: "r", kind: "build", what: "archery_range", building: "r#2", start: 140, end: 200 },
      { step_id: "a", kind: "produce", what: "archer", building: "r#1", start: 140, end: 175 },
      { step_id: "a", kind: "produce", what: "archer", building: "r#1", start: 175, end: 210 },
      { step_id: "a", kind: "produce", what: "archer", building: "r#2", start: 200, end: 235 },
      { step_id: "a", kind: "produce", what: "archer", building: "r#2", start: 235, end: 270 },
    ],
  };

  it("cada edificio que produce tiene su línea, con su construcción al principio y de a una cosa", () => {
    const lanes = lanesFor(steps, a, data);

    expect(lanes.map((l) => [l.id, l.label])).toEqual([
      [VILLAGERS_LANE, "Aldeanos"],
      ["town_center#1", "Centro urbano"],
      ["r#1", "Galería de tiro 1"],
      ["r#2", "Galería de tiro 2"],
    ]);
    // Las casas no producen: van a la línea de los aldeanos, una caja por paso; el movimiento también.
    expect(lanes[0].items.map((i) => [i.stepId, i.start, i.end])).toEqual([
      ["h", 0, 30],
      ["m", 90, 90],
    ]);
    // El centro urbano: los 3 aldeanos seguidos en una caja, después el telar.
    expect(lanes[1].items.map((i) => [i.stepId, i.label, i.start, i.end])).toEqual([
      ["v", "paso v", 0, 75],
      ["l", "paso l", 75, 100],
    ]);
    // Cada galería: su construcción y los arqueros que hizo ella (el paso se reparte entre las dos).
    expect(lanes[2].items.map((i) => [i.label, i.construction, i.start, i.end])).toEqual([
      ["Construir galería de tiro", true, 80, 140],
      ["2 arqueros", false, 140, 210],
    ]);
    expect(lanes[3].items.map((i) => [i.label, i.start, i.end])).toEqual([
      ["Construir galería de tiro", 140, 200],
      ["2 arqueros", 200, 270],
    ]);
  });

  it("la espera va en la primera caja del paso y un paso que no arranca va a la línea de los aldeanos", () => {
    const waiting: Analysis = { ...a, blocks: [{ step_id: "l", reason: "resources", from: 60, to: 75, missing: "gold" }] };
    expect(lanesFor(steps, waiting, data)[1].items[1].leadIn).toMatchObject({ from: 60, to: 75 });

    const stuck: Analysis = { ...a, steps: a.steps.map((s) => (s.id === "m" ? { ...s, start: null, end: null } : s)) };
    expect(lanesFor(steps, stuck, data)[0].items.find((i) => i.stepId === "m")).toMatchObject({ neverStarts: true, start: null });
  });

  it("sin simulación, cada paso va a la línea de su edificio en el orden de la BO", () => {
    const lanes = lanesFor(steps, null, data);
    expect(lanes.map((l) => l.label)).toEqual(["Aldeanos", "Centro urbano", "Galería de tiro"]);
    expect(lanes[2].items.map((i) => [i.stepId, i.index])).toEqual([
      ["r", 2],
      ["a", 4],
    ]);

    const placed = placeLanes(lanes, scaleFor(800, 600), "ordinal", 24);
    expect(placed.items.find((i) => i.stepId === "a")!.x0).toBe(PAD_L + 4 * ORDINAL_W);
  });

  it("las cajas de una línea van en filas solo si se pisan, y cada línea suma su alto", () => {
    const overlapping: Analysis = {
      ...a,
      jobs: [...(a.jobs ?? []), { step_id: "f", kind: "build", what: "farm", building: "f#1", start: 10, end: 40 }],
      buildings: [...(a.buildings ?? []), { id: "f#1", key: "farm", step_id: "f", built: 40 }],
    };
    const withFarm = [...steps, step("f", { do: "build", what: "farm", count: 1, assign: "farm" })];
    const placed = placeLanes(lanesFor(withFarm, overlapping, data), scaleFor(PAD_L + 608, 600), "time", 100);
    const villagers = placed.lanes[0];
    expect(villagers.rows).toBe(2); // casas (0 a 30) y granja (10 a 40) a la vez
    expect(placed.lanes[1].top).toBe(100 + 2 * LANE_H);
    expect(placed.items.filter((i) => i.lane === 0).map((i) => i.row).sort()).toEqual([0, 0, 1]);
  });
});

describe("paleta", () => {
  const data = JSON.parse(readFileSync(join(__dirname, "..", "..", "..", "public", "coach", "game_data.json"), "utf8"));
  const maps = parseMaps(readFileSync(join(__dirname, "..", "..", "..", "..", "data", "maps.json"), "utf8"));

  it("con cualquier civ y mapa trae todas las entidades, con los clics de edad primero", () => {
    const groups = paletteItems(data, "any");
    const keys = groups.flatMap((g) => g.items.map((i) => i.key));
    expect(groups.map((g) => g.title)).toEqual(["Aldeanos", "Mover", "Edificios", "Investigaciones", "Unidades"]);
    expect(groups[0].items).toHaveLength(8);
    expect(groups[0].items[0]).toMatchObject({ label: "Aldeanos a ovejas", step: { do: "train", what: "villager", assign: "sheep" } });
    expect(groups[3].items.slice(0, 3).map((i) => i.label)).toEqual(["Clic en Feudal", "Clic en Castillos", "Clic en Imperial"]);
    expect(keys.filter((k) => !k.startsWith("villagers:") && k !== "move")).toHaveLength(Object.keys(data.entities).length - 1); // sin el aldeano
    const ages = groups[2].items.map((i) => i.age);
    expect(ages).toEqual([...ages].sort((a, b) => ["dark", "feudal", "castle", "imperial"].indexOf(a) - ["dark", "feudal", "castle", "imperial"].indexOf(b)));
  });

  it("filtra por la civ y por el mapa", () => {
    const mapuche = paletteItems(data, "mapuche").flatMap((g) => g.items.map((i) => i.key));
    expect(mapuche).not.toContain("stable");
    expect(mapuche).not.toContain("scout_cavalry");
    expect(mapuche).toContain("settlement");

    const arabia = paletteItems(data, "any", maps.find((m) => m.key === "arabia")!);
    const keys = arabia.flatMap((g) => g.items.map((i) => i.key));
    expect(keys).not.toContain("villagers:fish");
    expect(keys).not.toContain("dock");
    expect(keys).not.toContain("fishing_ship");
    expect(keys).not.toContain("galley");
    expect(arabia[0].items).toHaveLength(7);

    const islands = paletteItems(data, "any", maps.find((m) => m.key === "islands")!).flatMap((g) => g.items.map((i) => i.key));
    expect(islands).toContain("dock");
    expect(islands).toContain("villagers:fish");
  });

  it("parseMaps lee los comentarios del archivo y pone Cualquiera primero", () => {
    expect(maps[0]).toMatchObject({ key: "any", name: "Cualquiera", water: true });
    expect(maps[0].sources).toHaveLength(8);
    expect(maps.find((m) => m.key === "arabia")).toMatchObject({ name: "Arabia", water: false });
    expect(parseMaps("")).toHaveLength(1);
    expect(parseMaps("{ roto")).toHaveLength(1);
  });
});
