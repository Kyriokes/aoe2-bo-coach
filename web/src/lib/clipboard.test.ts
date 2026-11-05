import { afterEach, describe, expect, it, vi } from "vitest";
import { copyText } from "./clipboard";

describe("copiar al portapapeles", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("dice si se pudo", async () => {
    const writeText = vi.fn(async () => {});
    vi.stubGlobal("navigator", { clipboard: { writeText } });
    expect(await copyText("k7m2qa")).toBe(true);
    expect(writeText).toHaveBeenCalledWith("k7m2qa");
  });

  it("sin permiso, no tira", async () => {
    vi.stubGlobal("navigator", { clipboard: { writeText: () => Promise.reject(new Error("NotAllowedError")) } });
    expect(await copyText("x")).toBe(false);
  });

  it("fuera de https el navegador ni siquiera tiene portapapeles", async () => {
    vi.stubGlobal("navigator", {});
    expect(await copyText("x")).toBe(false);
  });
});
