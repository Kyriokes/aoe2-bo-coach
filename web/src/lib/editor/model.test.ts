import { describe, expect, it } from "vitest";
import type { Analysis } from "../coach/types";
import { allIds, blockOf, fromAnalysis, groupNames, initialState, insertBlock, isBoundary, nextId, reduce, splitBlock, toJson, type EditorStep } from "./model";

/** Un análisis del Core armado a mano: la BO normalizada (schema 2, totales) y las unidades por paso. */
function analysis(steps: Record<string, unknown>[], units: number[], extra: Record<string, unknown> = {}): Analysis {
  return {
    ok: true,
    valid: true,
    id: "bo1",
    name: "Prueba",
    civ: "any",
    map: null,
    author: null,
    description: null,
    schema_version: 2,
    tags: ["feudal"],
    targets: {},
    build_order: { schema_version: 2, id: "bo1", name: "Prueba", civ: "any", tags: ["feudal"], steps, ...extra },
    issues: [],
    steps: steps.map((s, i) => ({
      id: s.id as string,
      text: "",
      do: s.do as "train",
      what: (s.what as string) ?? null,
      count: (s.count as number) ?? 1,
      units: units[i],
      note: null,
      start: null,
      end: null,
    })),
  };
}

const loaded = analysis(
  [
    { id: "s1", do: "build", what: "house", count: 2, builders: 3, assign: "sheep", after: [] },
    { id: "s2", do: "train", what: "villager", count: 9, assign: "sheep", after: [] },
    { id: "s3", do: "train", what: "villager", count: 4, assign: "berries", group: "molino1", after: [] },
    { id: "s4", do: "build", what: "mill", count: 1, group: "molino1", after: [] },
    { id: "s5", do: "move", count: 3, from: "sheep", assign: "wood", after: ["s2"], note: "Cuando se acaben" },
  ],
  [2, 6, 4, 1, 1],
  { posture: { queue: ["villager"] } },
);

function step(id: string, extra: Partial<EditorStep> = {}): EditorStep {
  return { id, do: "train", what: "villager", count: 1, assign: "sheep", after: [], withPrevious: false, ...extra };
}

describe("cargar y guardar", () => {
  it("pasa la BO del Core al modelo en unidades", () => {
    const { bo, steps } = fromAnalysis(loaded);

    expect(bo).toMatchObject({ id: "bo1", name: "Prueba", civ: "any", map: "any", tags: ["feudal"], posture: { queue: ["villager"] } });
    expect(steps.map((s) => s.count)).toEqual([2, 6, 4, 1, 3]); // aldeanos en unidades; move conserva su count
    expect(steps.map((s) => s.withPrevious)).toEqual([false, false, false, true, false]);
    expect(steps[3].groupName).toBe("molino1");
    expect(steps[4]).toMatchObject({ from: "sheep", assign: "wood", after: ["s2"], note: "Cuando se acaben" });
    expect(steps[0]).toMatchObject({ builders: 3, assign: "sheep" });
  });

  it("escribe schema 1 con los grupos rearmados y conserva el nombre del grupo", () => {
    const state = { ...initialState(), ...fromAnalysis(loaded) };

    const json = toJson(state) as { schema_version: number; steps: Record<string, unknown>[]; author?: string; posture?: unknown; map?: string };

    expect(json.schema_version).toBe(1);
    expect(json.steps.map((s) => s.count)).toEqual([2, 6, 4, 1, 3]);
    expect(json.steps.map((s) => s.group)).toEqual([undefined, undefined, "molino1", "molino1", undefined]);
    expect(json.steps[4]).toMatchObject({ from: "sheep", assign: "wood", after: ["s2"], note: "Cuando se acaben" });
    expect(json.author).toBeUndefined();
    expect(json.map).toBeUndefined();
    expect(json.posture).toEqual({ queue: ["villager"] });
  });

  it("un grupo nuevo toma el nombre del primero y el mapa se escribe si no es cualquiera", () => {
    const state = { ...initialState(), bo: { ...initialState().bo, map: "arabia" }, steps: [step("s1"), step("s2", { withPrevious: true }), step("s3", { withPrevious: true }), step("s4")] };

    expect(groupNames(state.steps)).toEqual(["gs1", "gs1", "gs1", undefined]);
    expect((toJson(state) as { map: string }).map).toBe("arabia");
  });
});

describe("cadenas e ids", () => {
  const steps = [step("s1"), step("s2", { withPrevious: true }), step("s3"), step("s4", { withPrevious: true }), step("s5", { withPrevious: true })];

  it("blockOf da la cadena entera e isBoundary solo deja insertar entre cadenas", () => {
    expect(blockOf(steps, "s2")).toEqual({ from: 0, to: 1 });
    expect(blockOf(steps, "s4")).toEqual({ from: 2, to: 4 });
    expect(blockOf(steps, "s3")).toEqual({ from: 2, to: 4 });
    expect(blockOf(steps, "x")).toBeNull();
    expect([0, 1, 2, 3, 4, 5].map((i) => isBoundary(steps, i))).toEqual([true, false, true, false, false, true]);
  });

  it("splitBlock e insertBlock mueven la cadena sin partir otra", () => {
    const { others, block } = splitBlock(steps, 2, 4);
    expect(others.map((s) => s.id)).toEqual(["s1", "s2"]);
    expect(block.map((s) => s.id)).toEqual(["s3", "s4", "s5"]);
    expect(insertBlock(others, block, 1).map((s) => s.id)).toEqual(["s3", "s4", "s5", "s1", "s2"]); // 1 cae dentro de s1+s2: va al borde de abajo
    expect(insertBlock(others, block, 2).map((s) => s.id)).toEqual(["s1", "s2", "s3", "s4", "s5"]);

    // Sacar el principio de una cadena: el que sigue pasa a ser principio.
    const split = splitBlock([step("a"), step("b", { withPrevious: true }), step("c")], 0, 0);
    expect(split.others[0]).toMatchObject({ id: "b", withPrevious: false });
  });

  it("nextId sigue al más alto, contando las ramas", () => {
    const state = { ...initialState(), steps: [step("s1"), step("s7"), step("x")] };
    expect(nextId(state)).toBe("s8");
    state.bo.branches = [{ on: "under_attack", steps: [{ id: "s9" }] }];
    expect(allIds(state)).toEqual(new Set(["s1", "s7", "x", "s9"]));
    expect(nextId(state)).toBe("s10");
    expect(nextId({ ...initialState(), steps: [] })).toBe("s1");
  });
});

describe("el reductor", () => {
  const base = { ...initialState(), ...fromAnalysis(loaded) };

  it("insertar, mover, borrar y cambiar la cantidad suben la revisión", () => {
    let state = reduce(base, { type: "insert", step: step("s6", { assign: "wood" }), index: 1 });
    expect(state.steps.map((s) => s.id)).toEqual(["s1", "s6", "s2", "s3", "s4", "s5"]);
    expect(state.selected).toBe("s6");
    expect(state.revision).toBe(base.revision + 1);

    state = reduce(state, { type: "move", blockFrom: 3, blockTo: 4, index: 0 });
    expect(state.steps.map((s) => s.id)).toEqual(["s3", "s4", "s1", "s6", "s2", "s5"]);

    state = reduce(state, { type: "remove", id: "s2" });
    expect(state.steps.map((s) => s.id)).toEqual(["s3", "s4", "s1", "s6", "s5"]);
    expect(state.steps[4].after).toEqual([]); // s5 dependía de s2

    state = reduce(state, { type: "setCount", id: "s6", count: 0 });
    expect(state.steps[3].count).toBe(1);
    state = reduce(state, { type: "update", id: "s6", patch: { assign: "gold", note: "x" } });
    expect(state.steps[3]).toMatchObject({ assign: "gold", note: "x" });
    expect(state.analyzedRevision).toBeLessThan(state.revision);
  });

  it("borrar el principio de una cadena deja al siguiente como principio con el nombre del grupo", () => {
    const state = reduce(base, { type: "remove", id: "s3" });
    expect(state.steps[2]).toMatchObject({ id: "s4", withPrevious: false, groupName: "molino1" });
  });

  it("junto con el anterior se prende y se apaga, nunca en el primero", () => {
    let state = reduce(base, { type: "toggleWithPrevious", id: "s4" });
    expect(state.steps[3].withPrevious).toBe(false);
    state = reduce(state, { type: "toggleWithPrevious", id: "s2" });
    expect(state.steps[1].withPrevious).toBe(true);
    expect(reduce(state, { type: "toggleWithPrevious", id: "s1" })).toBe(state);
  });

  it("commit confirma pasos y análisis, y un análisis viejo no pisa uno nuevo", () => {
    const state = reduce(base, { type: "commit", steps: base.steps.slice(0, 2), analysis: loaded, selected: "s2" });
    expect(state.steps).toHaveLength(2);
    expect(state.analyzedRevision).toBe(state.revision);
    expect(state.selected).toBe("s2");

    const edited = reduce(state, { type: "setField", field: "name", value: "Otra" });
    const stale = reduce(edited, { type: "analyzed", analysis: loaded, revision: edited.revision - 5 });
    expect(stale.analyzedRevision).toBe(state.revision);
    const fresh = reduce(edited, { type: "analyzed", analysis: loaded, revision: edited.revision });
    expect(fresh.analyzedRevision).toBe(edited.revision);
  });

  it("nueva arranca vacía con nombre y load vuelve a cargar", () => {
    const state = reduce(base, { type: "new", id: "n1" });
    expect(state.steps).toEqual([]);
    expect(state.bo.name).toBe("Nueva build order");
    expect(state.analysis).toBeNull();
    expect(reduce(state, { type: "load", analysis: loaded }).steps).toHaveLength(5);
  });
});
