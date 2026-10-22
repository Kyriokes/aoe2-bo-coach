import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { formatAmount, parseSupport, supportLinks, tally, type Support } from "./support";

const base: Support = {
  links: {},
  currency: "ARS",
  goals: [
    { id: "dominio", name: "Dominio", target: 1000 },
    { id: "hosting", name: "Hosting", target: 2000 },
  ],
  donations: [],
};

describe("las metas se van llenando con los aportes", () => {
  it("sin aportes, todo en cero", () => {
    const t = tally(base);
    expect(t.goals.map((g) => [g.raised, g.percent, g.done])).toEqual([
      [0, 0, false],
      [0, 0, false],
    ]);
    expect(t.total).toBe(0);
    expect(t.lastDate).toBeNull();
  });

  it("los aportes sin meta llenan las metas en orden y lo que sobra pasa a la siguiente", () => {
    const t = tally({ ...base, donations: [{ date: "2026-10-01", amount: 700 }, { date: "2026-10-05", amount: 800 }] });
    expect(t.goals.map((g) => [g.raised, g.percent, g.done])).toEqual([
      [1000, 100, true],
      [500, 25, false],
    ]);
    expect(t.total).toBe(1500);
    expect(t.count).toBe(2);
    expect(t.surplus).toBe(0);
    expect(t.lastDate).toBe("2026-10-05");
  });

  it("un aporte con meta va a esa meta aunque se pase, y con todo cumplido queda excedente", () => {
    const t = tally({ ...base, donations: [{ date: "2026-10-01", amount: 1500, goal: "hosting" }, { date: "2026-10-02", amount: 1200 }, { date: "2026-10-03", amount: 900 }] });
    expect(t.goals.map((g) => [g.raised, g.percent, g.done])).toEqual([
      [1000, 100, true],
      [2000, 100, true],
    ]);
    expect(t.surplus).toBe(600);
    // Una meta que no existe se trata como aporte sin meta.
    expect(tally({ ...base, donations: [{ date: "x", amount: 100, goal: "otra" }] }).goals[0].raised).toBe(100);
  });
});

describe("el archivo", () => {
  it("se lee con comentarios y lo roto se ignora", () => {
    const s = parseSupport(`// comentario
      { "links": { "cafecito": " ana ", "paypal": 7, "kofi": "" }, "currency": "usd",
        "goals": [ { "id": "a", "name": "A", "target": 100 }, { "id": "", "name": "sin id", "target": 5 }, { "id": "b", "name": "B", "target": -3 } ],
        "donations": [ { "date": "2026-01-01", "amount": 10, "from": "Beto", "note": "" }, { "amount": 0 }, "nada" ] }`);
    expect(s.links).toEqual({ cafecito: "ana" });
    expect(s.currency).toBe("USD");
    expect(s.goals.map((g) => g.id)).toEqual(["a"]);
    expect(s.donations).toEqual([{ date: "2026-01-01", amount: 10, from: "Beto" }]);
    expect(parseSupport("{ roto").goals).toEqual([]);
    expect(parseSupport("").currency).toBe("ARS");
  });

  it("el del repo tiene metas y los enlaces para completar", () => {
    const s = parseSupport(readFileSync(join(__dirname, "..", "..", "..", "data", "donaciones.json"), "utf8"));
    expect(s.goals.length).toBeGreaterThanOrEqual(1);
    expect(s.goals.every((g) => g.target > 0)).toBe(true);
    expect(s.currency).toBe("ARS");
  });
});

describe("enlaces para donar", () => {
  it("arma la dirección de cada sitio y saltea lo que no sirve", () => {
    const links = supportLinks({ cafecito: "ana", paypal: "ana/x", kofi: "beto", mercado_pago: "http://inseguro" });
    expect(links).toEqual([
      { site: "cafecito", label: "Cafecito", url: "https://cafecito.app/ana" },
      { site: "kofi", label: "Ko-fi", url: "https://ko-fi.com/beto" },
    ]);
    expect(supportLinks({ mercado_pago: "https://link.mercadopago.com.ar/ana" })[0].url).toBe("https://link.mercadopago.com.ar/ana");
    expect(supportLinks({})).toEqual([]);
  });

  it("formatea en pesos", () => {
    expect(formatAmount(30000, "ARS").replace(/\s/g, " ")).toMatch(/^\$ ?30\.000$/);
    expect(formatAmount(5, "XYZ")).toContain("XYZ");
  });
});
