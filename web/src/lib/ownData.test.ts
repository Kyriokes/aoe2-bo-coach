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

  it("las suscripciones se dan por desconocidas en vez de romper la página", async () => {
    const { mySubscriptionIds, mySubscriptions } = await import("./ownData");
    await expect(mySubscriptionIds()).resolves.toBeNull();
    await expect(mySubscriptions()).resolves.toEqual([]);
  });

  it("una BO propia que no se pudo leer es como una que no existe", async () => {
    const { getOwnBuildOrder } = await import("./ownData");
    await expect(getOwnBuildOrder("abcdef")).resolves.toBeNull();
  });
});
