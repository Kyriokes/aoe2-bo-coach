import { describe, expect, it } from "vitest";
import { parseBuildOrder } from "./buildOrder";

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
  it("rechaza una BO de más de 512 KB, como texto o como objeto", () => {
    const heavy = bo({ notes: "x".repeat(600_000) });
    expect(errorOf(heavy)).toContain("pesa");
    expect(errorOf(JSON.stringify(heavy))).toContain("pesa");
  });

  it("acepta una BO con notas largas pero razonables", () => {
    expect(errorOf(bo({ notes: "x".repeat(4000) }))).toBeNull();
  });
});
