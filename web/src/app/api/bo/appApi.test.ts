import { beforeEach, describe, expect, it, vi } from "vitest";

/** La publicación desde la app con la base y el caché falsos. */
const rpc = vi.fn();
const revalidateTag = vi.fn();

vi.mock("next/cache", () => ({ cacheLife: () => {}, cacheTag: () => {}, revalidateTag: (...args: unknown[]) => revalidateTag(...args) }));
vi.mock("@/lib/supabase/server", () => ({ publicClient: () => ({ rpc }) }));

const token = `bocoach_${"a".repeat(64)}`;
const buildOrder = { schema_version: 2, name: "Prueba", steps: [{ id: "a", do: "train", what: "villager" }] };

async function publish(body: string, code: string | null = null) {
  const { publishFromApp } = await import("./appApi");
  const request = new Request("https://sitio.example/api/bo", { method: "POST", headers: { authorization: `Bearer ${token}` }, body });
  return publishFromApp(request, code);
}

describe("publicar desde la app", () => {
  beforeEach(() => {
    rpc.mockReset();
    revalidateTag.mockReset();
    rpc.mockResolvedValue({ data: "abc234", error: null });
  });

  it("publica y devuelve el código", async () => {
    const response = await publish(JSON.stringify({ build_order: buildOrder, feudal_click: 500.4, castle_click: -1 }));
    expect(response.status).toBe(201);
    expect(await response.json()).toEqual({ code: "abc234", url: "https://sitio.example/bo/abc234" });
    expect(rpc).toHaveBeenCalledWith("app_publish", expect.objectContaining({ p_token: token, p_feudal_click: 500, p_castle_click: null, p_code: null }));
  });

  // Cada una daba 500: un error de TypeScript en el servidor o de la base.
  it.each([
    ["un cuerpo null", "null"],
    ["un cuerpo que es un número", "5"],
    ["un cuerpo que no es JSON", "{"],
  ])("%s da 400 sin llegar a la base", async (_name, body) => {
    expect((await publish(body)).status).toBe(400);
    expect(rpc).not.toHaveBeenCalled();
  });

  it("un clic de edad enorme da 400 sin llegar a la base", async () => {
    for (const click of ["feudal_click", "castle_click"]) {
      const response = await publish(JSON.stringify({ build_order: buildOrder, [click]: 1e10 }));
      expect(response.status).toBe(400);
      expect((await response.json()).error).toMatch(/86400/);
    }
    expect(rpc).not.toHaveBeenCalled();
    expect((await publish(JSON.stringify({ build_order: buildOrder, feudal_click: 86400 }))).status).toBe(201);
  });

  it("un carácter nulo en un texto da 400 sin llegar a la base", async () => {
    const response = await publish(JSON.stringify({ build_order: { ...buildOrder, description: "a\u0000b" } }));
    expect(response.status).toBe(400);
    expect((await response.json()).error).toMatch(/nulo/);
    expect(rpc).not.toHaveBeenCalled();
  });

  it("un dato que la base no acepta da 400 y no muestra el detalle", async () => {
    rpc.mockResolvedValue({ data: null, error: { code: "22003", message: 'value "10000000000" is out of range for type integer' } });
    const response = await publish(JSON.stringify({ build_order: buildOrder }));
    expect(response.status).toBe(400);
    expect((await response.json()).error).not.toMatch(/integer/);
  });

  it("sin el token no publica", async () => {
    const { publishFromApp } = await import("./appApi");
    const response = await publishFromApp(new Request("https://sitio.example/api/bo", { method: "POST", body: "{}" }), null);
    expect(response.status).toBe(401);
  });
});
