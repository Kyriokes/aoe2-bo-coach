import { redirect } from "next/navigation";
import { afterEach, describe, expect, it, vi } from "vitest";
import { safeAction } from "./safeAction";

describe("llamar a una acción de servidor", () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("devuelve lo que dice la acción, bien o mal", async () => {
    expect(await safeAction(async () => ({ ok: true as const, value: "k7m2qa" }), "falló")).toEqual({ ok: true, value: "k7m2qa" });
    expect(await safeAction(async () => ({ ok: false as const, error: "Esa BO no es tuya." }), "falló")).toEqual({ ok: false, error: "Esa BO no es tuya." });
  });

  // Se cortó la red o hay un deploy nuevo con la pestaña abierta: rechazaba dentro de startTransition y se perdía la página.
  it("un rechazo pasa a ser un error con el mensaje, sin llegar a React", async () => {
    const log = vi.spyOn(console, "error").mockImplementation(() => {});
    const result = await safeAction(() => Promise.reject(new TypeError("Failed to fetch")), "No se pudo completar.");
    expect(result).toEqual({ ok: false, error: "No se pudo completar." });
    expect(log).toHaveBeenCalled();
  });

  it("una acción que tira sin llegar a la promesa también", async () => {
    vi.spyOn(console, "error").mockImplementation(() => {});
    const action = (): Promise<{ ok: true; value: null }> => {
      throw new Error("boom");
    };
    expect(await safeAction(action, "No se pudo completar.")).toEqual({ ok: false, error: "No se pudo completar." });
  });

  it("lo que Next usa para navegar sigue de largo", async () => {
    const action = async (): Promise<{ ok: true; value: null }> => redirect("/cuenta");
    await expect(safeAction(action, "No se pudo completar.")).rejects.toMatchObject({ digest: expect.stringMatching(/^NEXT_REDIRECT/) });
  });
});
