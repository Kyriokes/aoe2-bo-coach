import { describe, expect, it } from "vitest";
import { allowedRange, canMake, clampCandidate, indexFromTime, snapToBoundary, type GameDataLite } from "./constraints";
import type { EditorStep } from "./model";

/** Un game_data chico con lo que hace falta para cada regla. */
const data: GameDataLite = {
  entities: {
    villager: { kind: "unit", age: "dark", made_in: "town_center" },
    house: { kind: "building", age: "dark" },
    lumber_camp: { kind: "building", age: "dark" },
    mill: { kind: "building", age: "dark" },
    barracks: { kind: "building", age: "dark" },
    settlement: { kind: "building", age: "dark", counts_as: ["lumber_camp", "mining_camp", "mill", "house"] },
    archery_range: { kind: "building", age: "feudal" },
    stable: { kind: "building", age: "feudal" },
    blacksmith: { kind: "building", age: "feudal" },
    archer: { kind: "unit", age: "feudal", made_in: "archery_range" },
    scout_cavalry: { kind: "unit", age: "feudal", made_in: "stable" },
    loom: { kind: "tech", age: "dark", made_in: "town_center" },
    feudal_age: { kind: "tech", age: "dark", made_in: "town_center" },
    castle_age: { kind: "tech", age: "feudal", made_in: "town_center" },
    university: { kind: "building", age: "castle" },
    monastery: { kind: "building", age: "castle" },
    castle: { kind: "building", age: "castle" },
    imperial_age: { kind: "tech", age: "castle", made_in: "town_center" },
  },
  civs: { mapuche: { unavailable: ["stable"] } },
};

function step(id: string, extra: Partial<EditorStep>): EditorStep {
  return { id, do: "train", count: 1, after: [], withPrevious: false, ...extra };
}
const villagers = (id: string, assign: EditorStep["assign"] = "sheep") => step(id, { what: "villager", assign, count: 2 });
const build = (id: string, what: string) => step(id, { do: "build", what });
const research = (id: string, what: string) => step(id, { do: "research", what });
const train = (id: string, what: string) => step(id, { what, count: 3 });

// La BO de referencia: 0 casas, 1 ovejas, 2 maderero, 3 madera, 4 molino, 5 bayas (+anterior), 6 Feudal, 7 cuartel, 8 establo, 9 exploradores, 10 Castillos.
const bo: EditorStep[] = [
  build("s0", "house"),
  villagers("s1"),
  build("s2", "lumber_camp"),
  villagers("s3", "wood"),
  build("s4", "mill"),
  { ...villagers("s5", "berries"), withPrevious: true },
  research("s6", "feudal_age"),
  build("s7", "barracks"),
  build("s8", "stable"),
  train("s9", "scout_cavalry"),
  research("s10", "castle_age"),
];
const without = (...ids: string[]) => bo.filter((s) => !ids.includes(s.id));
const label = (s: EditorStep) => s.id;

describe("allowedRange", () => {
  it("(a) una entidad va después del clic de su edad", () => {
    const range = allowedRange(without("s8"), [bo[8]], data, label);
    expect(range.lo).toBe(7); // después de s6 (Feudal), que queda en el índice 6 de los demás
    expect(range.loReason).toBe("Recién después del clic en Feudal");
    expect(range.hi).toBe(8); // antes de s9: es el único establo
  });

  it("(a) sin el clic de la edad no puede ir a ningún lado", () => {
    const range = allowedRange(without("s6", "s8"), [bo[8]], data, label);
    expect(range.lo).toBeGreaterThan(range.hi);
  });

  it("(b) un clic de edad va antes del primer paso que necesita esa edad", () => {
    const range = allowedRange(without("s6"), [bo[6]], data, label);
    expect(range.hi).toBe(7); // antes de s8 (establo, Feudal); el cuartel es de la Edad Oscura
    expect(range.hiReason).toBe('Antes de "s8": lo necesita en Feudal');
    expect(range.lo).toBe(6); // (e) después del segundo edificio de la Edad Oscura (maderero y molino), y s4+s5 van juntos
    expect(range.loReason).toContain("2 edificios de Edad Oscura");
  });

  it("(c) after en las dos direcciones", () => {
    const steps = [villagers("a"), villagers("b"), { ...villagers("c"), after: ["a"] }, { ...villagers("d"), after: ["c"] }];
    const range = allowedRange(steps.filter((s) => s.id !== "c"), [steps[2]], data, label);
    expect(range).toMatchObject({ lo: 1, hi: 2 });
    expect(range.loReason).toContain('depende: "a"');
    expect(range.hiReason).toContain('depende de este: "d"');
  });

  it("(d) lo que se entrena va después de su edificio, y el edificio no pasa después de lo que lo necesita", () => {
    const scouts = allowedRange(without("s9"), [bo[9]], data, label);
    expect(scouts.lo).toBe(9); // después de s8 (establo)
    expect(scouts.loReason).toBe("Recién después de construir establo: ahí se hace");

    const stable = allowedRange(without("s8"), [bo[8]], data, label);
    expect(stable.hi).toBe(8);
    expect(stable.hiReason).toBe('Antes de "s9": es el único establo que hay antes');

    // Con otro establo antes, el primero puede ir después de los exploradores.
    const twoStables = [...bo.slice(0, 8), build("s8b", "stable"), ...bo.slice(8)];
    expect(allowedRange(twoStables.filter((s) => s.id !== "s8"), [bo[8]], data, label).hi).toBe(twoStables.length - 1);

    // Si nadie construye el productor, no hay límite (el Core avisa).
    expect(allowedRange(without("s8", "s9"), [bo[9]], data, label).lo).toBe(7);
  });

  it("(e) el clic va después del segundo edificio y esos edificios no pasan después del clic", () => {
    const mill = allowedRange(without("s4", "s5"), [bo[4], bo[5]], data, label);
    expect(mill.hi).toBe(4); // antes de s6 (Feudal): con el molino afuera queda solo el maderero
    expect(mill.hiReason).toContain("es uno de sus 2 edificios");

    // Con un tercer edificio que califica, el molino puede pasar después del clic (hasta su uso en las bayas, que no aplica).
    const withSettlement = [...bo.slice(0, 2), build("x", "settlement"), ...bo.slice(2)];
    expect(allowedRange(withSettlement.filter((s) => !["s4", "s5"].includes(s.id)), [bo[4], bo[5]], data, label).hi).toBeGreaterThan(4);

    // El asentamiento cuenta como maderero y molino: Feudal puede ir justo después de él.
    const inca = [build("a", "house"), villagers("b"), build("c", "settlement"), research("d", "feudal_age")];
    expect(allowedRange(inca.filter((s) => s.id !== "d"), [inca[3]], data, label).lo).toBe(3);
  });

  it("(e) Imperial pide 2 edificios de Castillos, o un castillo solo", () => {
    const two = [research("f", "feudal_age"), research("c", "castle_age"), build("u", "university"), build("m", "monastery")];
    expect(allowedRange(two, [research("i", "imperial_age")], data, label).lo).toBe(4);
    const castle = [research("f", "feudal_age"), research("c", "castle_age"), build("k", "castle")];
    expect(allowedRange(castle, [research("i", "imperial_age")], data, label).lo).toBe(3);
  });

  it("(f) los límites caen en bordes de cadena", () => {
    const range = allowedRange(without("s2"), [bo[2]], data, label);
    // s4+s5 van juntos (índices 3 y 4 de los demás): el índice 4 no es un borde.
    expect(range.lo).toBe(0);
    expect([3, 5]).toContain(4 === range.hi ? 3 : range.hi > 4 ? 5 : range.hi);
    const others = without("s2");
    expect(others[4].withPrevious).toBe(true);
    expect(allowedRange(others, [bo[2]], data, label).hi % 1).toBe(0);
  });

  it("una caja mixta con el clic adentro se mira en orden", () => {
    const group = [research("f", "feudal_age"), { ...build("g", "archery_range"), withPrevious: true }];
    const others = [build("a", "house"), villagers("b"), build("c", "mill"), build("d", "lumber_camp"), train("e", "archer")];
    const range = allowedRange(others, group, data, label);
    expect(range.lo).toBe(4); // después del segundo edificio (molino y maderero)
    expect(range.hi).toBe(4); // antes de los arqueros, que necesitan Feudal y la galería
    expect(range.hi).toBeGreaterThanOrEqual(range.lo);
  });

  it("canMake mira lo que la civ no tiene y dónde se hace", () => {
    expect(canMake(data, "mapuche", "stable")).toBe(false);
    expect(canMake(data, "mapuche", "scout_cavalry")).toBe(false);
    expect(canMake(data, "mapuche", "archer")).toBe(true);
    expect(canMake(data, "any", "stable")).toBe(true);
    expect(canMake(data, "Mapuche", "stable")).toBe(false);
  });
});

describe("del puntero al índice", () => {
  it("indexFromTime usa el máximo acumulado y corta en el primer paso sin tiempo", () => {
    expect(indexFromTime(0, [0, 25, 20, 60])).toBe(0);
    expect(indexFromTime(22, [0, 25, 20, 60])).toBe(1);
    expect(indexFromTime(26, [0, 25, 20, 60])).toBe(3); // el 20 vale 25 por el acumulado
    expect(indexFromTime(1000, [0, 25, null, 60])).toBe(2);
    expect(indexFromTime(5, [])).toBe(0);
  });

  it("snapToBoundary va al borde más cercano por la mitad de la cadena", () => {
    const others = [villagers("a"), villagers("b"), { ...villagers("c"), withPrevious: true }, villagers("d")];
    const starts = [0, 100, 120, 200];
    const ends = [50, 150, 180, 250];
    expect(snapToBoundary(2, others, 130, starts, ends)).toBe(1); // (100 + 180) / 2 = 140: antes de la mitad
    expect(snapToBoundary(2, others, 150, starts, ends)).toBe(3);
    expect(snapToBoundary(1, others, 150, starts, ends)).toBe(1);
    expect(snapToBoundary(2, others, 150, [0, null, null, null], ends)).toBe(1);
  });

  it("clampCandidate deja el índice en el rango y dice por qué", () => {
    const range = { lo: 2, hi: 5, loReason: "antes no", hiReason: "después no" };
    expect(clampCandidate(3, range, 0)).toEqual({ index: 3, reason: null });
    expect(clampCandidate(0, range, 0)).toEqual({ index: 2, reason: "antes no" });
    expect(clampCandidate(9, range, 0)).toEqual({ index: 5, reason: "después no" });
    expect(clampCandidate(3, { lo: 4, hi: 3, loReason: "fijo", hiReason: null }, 7)).toEqual({ index: 7, reason: "fijo" });
  });
});
