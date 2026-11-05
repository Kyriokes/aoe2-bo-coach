import { afterEach, describe, expect, it, vi } from "vitest";
import { loadExample, loadExamples, pickExample } from "./examples";

describe("qué ejemplo se muestra", () => {
  const list = [
    { file: "arqueros.json", name: "Arqueros Feudal" },
    { file: "scouts.json", name: "Scouts Feudal (22 pop)" },
  ];

  it("de entrada la de scouts, que es la más corta de leer; si no hay, la primera", () => {
    expect(pickExample(list, "")).toBe("scouts.json");
    expect(pickExample([list[0]], "")).toBe("arqueros.json");
    expect(pickExample([], "")).toBe("");
  });

  // Next vuelve a mostrar la portada con sus efectos de nuevo (Activity): antes volvía siempre a la de scouts.
  it("la que se eligió sigue elegida al volver", () => {
    expect(pickExample(list, "arqueros.json")).toBe("arqueros.json");
  });

  it("si la elegida ya no está, vuelve a la de entrada", () => {
    expect(pickExample(list, "otra.json")).toBe("scouts.json");
  });
});

describe("los ejemplos de la app", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const respond = (status: number, body: string) => vi.stubGlobal("fetch", vi.fn(async () => new Response(body, { status })));

  it("trae la lista y cada ejemplo", async () => {
    respond(200, '[{"file":"a.json","name":"A"}]');
    expect(await loadExamples()).toEqual([{ file: "a.json", name: "A" }]);
    respond(200, '{"name":"A"}');
    expect(await loadExample("a.json")).toBe('{"name":"A"}');
  });

  // Antes el HTML de un 404 llegaba al editor como si fuera la BO.
  it("una respuesta de error es un error, no el texto de una BO", async () => {
    respond(404, "<html>no está</html>");
    await expect(loadExample("a.json")).rejects.toThrow("404");
    await expect(loadExamples()).rejects.toThrow("404");
  });

  it("sin red, el error llega a quien lo pidió", async () => {
    vi.stubGlobal("fetch", vi.fn(() => Promise.reject(new TypeError("Failed to fetch"))));
    await expect(loadExample("a.json")).rejects.toThrow("Failed to fetch");
  });
});
