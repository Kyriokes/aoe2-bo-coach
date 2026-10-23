import { describe, expect, it } from "vitest";
import type { GameStart } from "../coach/types";
import type { EditorStep } from "./model";
import { detectOpening, placeNew, STANDARD_START, villagersAfter, withOpening } from "./opening";

const step = (id: string, extra: Partial<EditorStep>): EditorStep => ({ id, do: "train", count: 1, after: [], withPrevious: false, ...extra });
const houses = step("s1", { do: "build", what: "house", count: 2, builders: 3, assign: "sheep" });
const villagers = (id: string, count: number, assign: EditorStep["assign"]) => step(id, { what: "villager", count, assign });
const huns: GameStart = { ...STANDARD_START, wood: 100, population_cap: 200, houses: false };
const chinese: GameStart = { ...STANDARD_START, villagers: { sheep: 3 } };

describe("la apertura (los aldeanos iniciales)", () => {
  it("reconoce las 2 casas, el envío directo y lo armado a mano", () => {
    expect(detectOpening([houses, villagers("s2", 3, "sheep")], STANDARD_START)).toEqual({ kind: "houses", source: "sheep" });
    expect(detectOpening([step("a", { do: "move", count: 3, assign: "wood" })], STANDARD_START)).toEqual({ kind: "direct", source: "wood" });
    expect(detectOpening([villagers("a", 3, "sheep")], STANDARD_START).kind).toBe("custom");
    expect(detectOpening([], STANDARD_START).kind).toBe("custom");
    // Casas con 2 constructores no son la apertura de los 3 iniciales.
    expect(detectOpening([{ ...houses, builders: 2 }], STANDARD_START).kind).toBe("custom");
  });

  it("cambiar la apertura reemplaza el primer paso (con su id) o lo agrega", () => {
    const rest = [villagers("s2", 3, "sheep")];
    const direct = withOpening([houses, ...rest], { kind: "direct", source: "berries" }, STANDARD_START, "s9");
    expect(direct).toEqual([step("s1", { do: "move", count: 3, assign: "berries" }), ...rest]);

    const added = withOpening(rest, { kind: "houses", source: "sheep" }, STANDARD_START, "s9");
    expect(added[0]).toMatchObject({ id: "s9", do: "build", what: "house", count: 2, builders: 3, assign: "sheep" });
    expect(added.slice(1)).toEqual(rest);

    expect(withOpening([houses, ...rest], { kind: "custom", source: "sheep" }, STANDARD_START, "s9")).toEqual(rest);
  });

  it("una civ sin casas manda directo aunque se pida casas", () => {
    expect(withOpening([], { kind: "houses", source: "sheep" }, huns, "s1")).toEqual([step("s1", { do: "move", count: 3, assign: "sheep" })]);
  });
});

describe("de dónde salen los aldeanos", () => {
  it("cuenta los iniciales, los de las casas y los nuevos, en unidades", () => {
    expect(villagersAfter([], STANDARD_START)).toMatchObject({ idle: 3, on: { sheep: 0 } });
    expect(villagersAfter([houses, villagers("s2", 3, "sheep"), villagers("s3", 4, "wood")], STANDARD_START)).toMatchObject({ idle: 0, on: { sheep: 6, wood: 4 } });
    expect(villagersAfter([step("m", { do: "move", count: 2, from: "sheep", assign: "gold" })], chinese)).toMatchObject({ idle: 3, on: { sheep: 1, gold: 2 } });
  });

  it("una caja nueva toma primero a los sin tarea, después la madera o la fuente con más gente", () => {
    const camp = step("n", { do: "build", what: "lumber_camp", from: "wood", assign: "wood" });
    expect(placeNew(camp, [], STANDARD_START)).toMatchObject({ from: undefined, assign: "wood" });
    expect(placeNew(camp, [houses, villagers("s2", 3, "sheep")], STANDARD_START)).toMatchObject({ from: "sheep", assign: "sheep" });
    expect(placeNew(camp, [houses, villagers("s3", 4, "wood")], STANDARD_START)).toMatchObject({ from: "wood", assign: "wood" });

    const farm = step("f", { do: "build", what: "farm", count: 2, from: "sheep", assign: "farm" });
    expect(placeNew(farm, [houses, villagers("s3", 4, "berries")], STANDARD_START)).toMatchObject({ from: "berries", assign: "farm" });

    const move = step("m", { do: "move", from: "sheep", assign: "wood" });
    expect(placeNew(move, [], STANDARD_START)).toMatchObject({ from: undefined, assign: "wood" });
    expect(placeNew(move, [houses], STANDARD_START)).toMatchObject({ from: "sheep", assign: "wood" });
    expect(placeNew(step("m2", { do: "move", from: "sheep", assign: "wood" }), [houses, step("x", { do: "move", count: 3, from: "sheep", assign: "wood" })], STANDARD_START)).toMatchObject({ from: "wood", assign: "gold" });

    const army = villagers("v", 1, "gold");
    expect(placeNew(army, [], STANDARD_START)).toBe(army);
  });
});
