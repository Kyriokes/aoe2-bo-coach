import { afterEach, describe, expect, it, vi } from "vitest";
import { loadExample, loadExamples } from "./examples";

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
