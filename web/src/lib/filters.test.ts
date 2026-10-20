import { describe, expect, it } from "vitest";
import { filterValue } from "./filters";

// civ y tag van tal cual al filtro de PostgREST (eq.<civ>, cs.{<tag>}): una llave, una coma o comillas rompen la
// sintaxis y la API respondía 500 (/api/bo?tag=a},b). Se acepta solo lo que puede tener un nombre de civ o una etiqueta.
describe("valores de los filtros civ y tag", () => {
  it("acepta letras con acentos, números, espacios y - _ . +", () => {
    expect(filterValue("feudal")).toBe("feudal");
    expect(filterValue("fast castle")).toBe("fast castle");
    expect(filterValue("22 pop")).toBe("22 pop");
    expect(filterValue("c++")).toBe("c++");
    expect(filterValue("v2.1_beta-x")).toBe("v2.1_beta-x");
    expect(filterValue("Arqueros rápidos")).toBe("Arqueros rápidos");
  });

  it("recorta espacios y descarta lo vacío o demasiado largo", () => {
    expect(filterValue("  arabia ")).toBe("arabia");
    expect(filterValue("   ")).toBeUndefined();
    expect(filterValue(undefined)).toBeUndefined();
    expect(filterValue("a".repeat(41))).toBeUndefined();
    expect(filterValue("a".repeat(40))).toBe("a".repeat(40));
  });

  it("rechaza lo que rompe la sintaxis del filtro", () => {
    for (const bad of ["a},b", "a,b", '"feudal"', "x\u005Cy", "(a)", "a|b", "a;b", "{feudal}"]) {
      expect(filterValue(bad), bad).toBeUndefined();
    }
  });
});
