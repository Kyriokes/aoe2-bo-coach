import { beforeEach, describe, expect, it, vi } from "vitest";

/** Las acciones con un cliente de Supabase falso: lo que importa acá es qué le piden a la base. */
const rpc = vi.fn();
const client = {
  auth: { getClaims: async () => ({ data: { claims: { sub: "11111111-1111-1111-1111-111111111111" } } }) },
  rpc,
};

vi.mock("next/cache", () => ({ cacheLife: () => {}, cacheTag: () => {}, updateTag: () => {} }));
vi.mock("next/navigation", () => ({ redirect: () => {} }));
vi.mock("@/lib/supabase/server", () => ({ sessionClient: async () => client, publicClient: () => null }));

describe("crear un token de la app", () => {
  beforeEach(() => rpc.mockReset());

  it("el de Conectar la app no publica", async () => {
    rpc.mockResolvedValue({ data: "bocoach_x", error: null });
    const { createAppToken } = await import("./actions");

    expect(await createAppToken("BO Coach", "es", false)).toEqual({ ok: true, value: "bocoach_x" });
    expect(rpc).toHaveBeenCalledWith("create_app_token", { p_label: "BO Coach", p_can_publish: false });
  });

  it("el que se crea a mano no le dice nada a la base y queda como siempre", async () => {
    rpc.mockResolvedValue({ data: "bocoach_x", error: null });
    const { createAppToken } = await import("./actions");

    await createAppToken("  Mi PC ", "es");
    expect(rpc).toHaveBeenCalledWith("create_app_token", { p_label: "Mi PC" });
  });

  it("con una base que todavía no tiene la migración, sigue andando", async () => {
    rpc.mockResolvedValueOnce({ data: null, error: { code: "PGRST202", message: "Could not find the function" } });
    rpc.mockResolvedValueOnce({ data: "bocoach_x", error: null });
    const { createAppToken } = await import("./actions");

    expect(await createAppToken("BO Coach", "es", false)).toEqual({ ok: true, value: "bocoach_x" });
    expect(rpc).toHaveBeenLastCalledWith("create_app_token", { p_label: "BO Coach" });
  });
});
