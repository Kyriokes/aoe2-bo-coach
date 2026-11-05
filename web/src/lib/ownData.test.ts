import { beforeEach, describe, expect, it, vi } from "vitest";

/**
 * ownData con un cliente de Supabase falso. Lo que lee la sesión solo alimenta botones ("Usar en el coach"): si la
 * base falla (por ejemplo, antes de correr la migración de suscripciones) la página tiene que seguir andando.
 */
const failing = { data: null, error: { message: 'relation "public.subscriptions" does not exist', code: "42P01" } };
const query = {
  select: () => query,
  eq: () => query,
  order: () => Promise.resolve(failing),
  maybeSingle: () => Promise.resolve(failing),
  then: (resolve: (value: typeof failing) => unknown) => resolve(failing),
};
const client = {
  auth: { getClaims: async () => ({ data: { claims: { sub: "11111111-1111-1111-1111-111111111111" } } }) },
  from: () => query,
};

vi.mock("next/cache", () => ({ cacheLife: () => {}, cacheTag: () => {} }));
vi.mock("./supabase/env", () => ({ supabaseConfigured: true, supabaseUrl: "", supabaseKey: "" }));
vi.mock("./supabase/server", () => ({ sessionClient: async () => client, publicClient: () => null }));

describe("lo propio con la base fallando", () => {
  beforeEach(() => {
    vi.spyOn(console, "error").mockImplementation(() => {});
  });

  it("las suscripciones de los botones se dan por desconocidas en vez de romper la página", async () => {
    const { mySubscriptionIds } = await import("./ownData");
    await expect(mySubscriptionIds()).resolves.toBeNull();
  });

  it("una BO propia que no se pudo leer es como una que no existe", async () => {
    const { getOwnBuildOrder } = await import("./ownData");
    await expect(getOwnBuildOrder("abcdef")).resolves.toBeNull();
  });

  // Decir "todavía no publicaste ninguna" con la base caída hace creer que se perdió todo: se ve la página de error.
  it("Mi cuenta tira el error en vez de decir que no hay nada", async () => {
    const { myAccountLists, mySubscriptions } = await import("./ownData");
    await expect(myAccountLists("11111111-1111-1111-1111-111111111111")).rejects.toThrow(/does not exist/);
    await expect(mySubscriptions()).rejects.toThrow(/does not exist/);
  });
});
