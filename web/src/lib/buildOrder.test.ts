import { describe, expect, it } from "vitest";
import { columnsFor, parseBuildOrder } from "./buildOrder";

/** Una BO mínima con un paso de aldeanos y lo que se quiera cambiar de ese paso. */
const bo = (step: Record<string, unknown>) => ({
  schema_version: 2,
  name: "Prueba",
  steps: [{ id: "a", do: "train", what: "villager", assign: "sheep", ...step }],
});

function errorOf(json: unknown) {
  const result = parseBuildOrder(json);
  return result.ok ? null : result.error;
}

// Sin tope, una BO publicada con un número enorme agota la memoria del simulador de cualquiera que abra su página
// (el Planner encola una entrada por unidad) y de la app de quien la importe.
describe("tope de count y builders", () => {
  it("rechaza un count mayor a 200", () => {
    expect(errorOf(bo({ count: 2_000_000_000 }))).toContain("count");
    expect(errorOf(bo({ count: 201 }))).toContain("count");
  });

  it("rechaza builders mayor a 200 y count menor a 1", () => {
    expect(errorOf(bo({ do: "build", what: "house", count: 1, builders: 201 }))).toContain("builders");
    expect(errorOf(bo({ count: 0 }))).toContain("count");
  });

  it("acepta hasta 200 y un paso sin count", () => {
    expect(errorOf(bo({ count: 200 }))).toBeNull();
    expect(errorOf(bo({}))).toBeNull();
  });
});

// Sin tope, los pasos admiten notas de cualquier largo: una BO de varios megas la bajaría cada visita a su página.
describe("ramas", () => {
  const withBranch = (count: number) => ({
    ...bo({}),
    branches: [{ on: "enemy_feudal_click", steps: [{ id: "r1", do: "train", what: "archer", count }] }],
  });

  it("los pasos de las ramas tienen el mismo tope", () => {
    expect(errorOf(withBranch(6))).toBeNull();
    expect(errorOf(withBranch(999_999))).toContain("branches");
  });
});

describe("tamaño de la BO", () => {
  it("rechaza una BO de más de 128 KB, como texto o como objeto", () => {
    const sized = (chars: number) => bo({ notes: "x".repeat(chars - JSON.stringify(bo({ notes: "" })).length) });
    expect(errorOf(sized(128_000))).toBeNull();
    expect(errorOf(JSON.stringify(sized(128_000)))).toBeNull();
    expect(errorOf(sized(128_001))).toContain("128 KB");
    expect(errorOf(JSON.stringify(sized(128_001)))).toContain("128 KB");
  });

  it("acepta una BO con notas largas pero razonables", () => {
    expect(errorOf(bo({ notes: "x".repeat(4000) }))).toBeNull();
  });
});

// Sin tope, una BO pública con 90.000 after o 20.000 objetivos congela la pestaña de quien la abre, y el mapa viaja
// entero en la lista de cada app.
describe("topes de after, objetivos y mapa", () => {
  const list = (n: number) => Array.from({ length: n }, (_, i) => `s${i}`);

  it("hasta 50 after por paso", () => {
    expect(errorOf(bo({ after: list(50) }))).toBeNull();
    expect(errorOf(bo({ after: null }))).toBeNull();
    expect(errorOf(bo({ after: list(51) }))).toMatch(/más de 50 pasos/);
    expect(errorOf(bo({ after: "s1" }))).toContain("after");
  });

  it("también en las ramas", () => {
    const branch = { on: "under_attack", steps: [{ id: "r1", do: "train", what: "archer", after: list(51) }] };
    expect(errorOf({ ...bo({}), branches: [branch] })).toMatch(/más de 50 pasos/);
  });

  it("hasta 40 objetivos", () => {
    const goals = (n: number) => Array.from({ length: n }, () => ({ age: "feudal_age" }));
    expect(errorOf({ ...bo({}), assisted: { goals: goals(40) } })).toBeNull();
    expect(errorOf({ ...bo({}), assisted: null })).toBeNull();
    expect(errorOf({ ...bo({}), assisted: { goals: goals(41) } })).toMatch(/más de 40 objetivos/);
  });

  it("el mapa hasta 40 caracteres", () => {
    expect(errorOf({ ...bo({}), map: "x".repeat(40) })).toBeNull();
    expect(errorOf({ ...bo({}), map: null })).toBeNull();
    expect(errorOf({ ...bo({}), map: "x".repeat(41) })).toMatch(/más de 40 caracteres/);
  });

  it("en el idioma de la página", () => {
    const result = parseBuildOrder(bo({ after: list(51) }), "en");
    expect(result.ok ? null : result.error).toMatch(/more than 50 steps/);
  });
});

describe("visibilidad", () => {
  const body = parseBuildOrder({ schema_version: 1, name: "x", steps: [{ id: "a", do: "train", what: "villager" }] });

  it("se guarda solo si se la pasa", () => {
    if (!body.ok) throw new Error(body.error);
    expect(columnsFor(body.body, 1, 2, "private").visibility).toBe("private");
    expect("visibility" in columnsFor(body.body, 1, 2)).toBe(false);
  });
});
