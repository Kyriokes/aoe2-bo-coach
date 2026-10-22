import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import type { Analysis } from "../coach/types";
import { paletteItems, parseMaps } from "./gameData";
import { boxesFor, GAP_PX, MIN_BOX_PX, ORDINAL_W, PAD_L, packLanes, scaleFor, timeSpan } from "./layout";
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

  it("scaleFor pasa de segundos a píxeles y vuelve", () => {
    const scale = scaleFor(1016, 600);
    expect(scale.x(0)).toBe(PAD_L);
    expect(scale.x(600)).toBe(1016 - 8);
    expect(scale.t(scale.x(123))).toBeCloseTo(123);
  });
});

describe("cajas y carriles", () => {
  const steps = [step("a"), step("b"), step("c", { withPrevious: true }), step("d")];

  it("una cadena es una sola caja con el tramo rayado del primer bloqueo", () => {
    const a = analysis({ a: [0, 50], b: [60, 100], c: [80, 130], d: [null, null] }, [{ step_id: "c", reason: "resources", from: 40, to: 80, missing: "wood" }]);
    const boxes = boxesFor(steps, a);

    expect(boxes.map((b) => b.ids)).toEqual([["a"], ["b", "c"], ["d"]]);
    expect(boxes[1]).toMatchObject({ label: "paso b + paso c", start: 60, end: 130, from: 1, to: 2 });
    expect(boxes[1].leadIn).toMatchObject({ from: 40, to: 60 });
    expect(boxes[2].neverStarts).toBe(true);
    expect(boxesFor(steps, null)[2].neverStarts).toBe(false);
  });

  it("packLanes pone en otro carril lo que se pisa y respeta el ancho mínimo", () => {
    const scale = scaleFor(616, 600); // 1 px por segundo
    const a = analysis({ a: [0, 100], b: [50, 120], c: [50, 120], d: [130, 131] });
    const { boxes, laneCount } = packLanes(boxesFor(steps, a), scale, "time");

    expect(boxes.map((b) => b.lane)).toEqual([0, 1, 0]);
    expect(laneCount).toBe(2);
    expect(boxes[2].x1 - boxes[2].x0).toBe(MIN_BOX_PX);
    expect(boxes[1].x0 - boxes[0].x0).toBe(50);
    expect(boxes[2].x0).toBeGreaterThanOrEqual(boxes[0].x1 + GAP_PX);
  });

  it("en modo ordinal van una al lado de la otra", () => {
    const { boxes, laneCount } = packLanes(boxesFor(steps, null), scaleFor(600, 600), "ordinal");
    expect(laneCount).toBe(1);
    expect(boxes.map((b) => b.x0)).toEqual([PAD_L, PAD_L + ORDINAL_W, PAD_L + 2 * ORDINAL_W]);
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
