import { describe, expect, it } from "vitest";
import { whatToLoad } from "./startup";

describe("qué carga el editor al arrancar", () => {
  it("la primera vez carga lo que manda la página: una BO para editar o una nueva", () => {
    expect(whatToLoad(null, '{"name":"x"}')).toBe("initial");
    expect(whatToLoad(null, "")).toBe("initial");
  });

  // Next deja la ruta en <Activity hidden> y al volver con Atrás recrea los efectos: antes, una BO nueva sin aviso.
  it("cuando la ruta vuelve a mostrarse no carga otra vez lo mismo", () => {
    expect(whatToLoad("", "")).toBe("nothing");
    expect(whatToLoad('{"name":"x"}', '{"name":"x"}')).toBe("nothing");
  });

  it("después de guardar (null otra vez) arranca de nuevo, y si el servidor manda otra versión la carga", () => {
    expect(whatToLoad(null, "")).toBe("initial");
    expect(whatToLoad('{"name":"x"}', '{"name":"y"}')).toBe("initial");
  });
});
