import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { parseMaps } from "./gameData";
import type { EditorStep } from "./model";
import { allItems, paletteBar, paletteHere, searchPalette, unavailableHere } from "./palette";

const data = JSON.parse(readFileSync(join(__dirname, "..", "..", "..", "public", "coach", "game_data.json"), "utf8"));
const maps = parseMaps(readFileSync(join(__dirname, "..", "..", "..", "..", "data", "maps.json"), "utf8"));

const step = (id: string, fields: Partial<EditorStep> & Pick<EditorStep, "do">): EditorStep => ({ id, count: 1, after: [], withPrevious: false, ...fields });

/** Casas, ovejas, campamento maderero, madera, cuartel, clic en Feudal y galería de tiro. */
const BO: EditorStep[] = [
  step("s1", { do: "build", what: "house", count: 2, builders: 3, assign: "sheep" }),
  step("s2", { do: "train", what: "villager", count: 3, assign: "sheep" }),
  step("s3", { do: "build", what: "lumber_camp", from: "sheep", assign: "wood" }),
  step("s4", { do: "train", what: "villager", count: 4, assign: "wood" }),
  step("s5", { do: "build", what: "barracks", from: "wood", assign: "wood" }),
  step("s6", { do: "research", what: "feudal_age" }),
  step("s7", { do: "build", what: "archery_range", from: "wood", assign: "wood" }),
];

describe("paleta como en el juego", () => {
  const bar = paletteBar(data, "any");
  const item = (key: string) => allItems(bar).find((i) => i.key === key)!;

  it("tiene aldeanos y edades arriba, los edificios con la economía primero y una pestaña por edificio que produce", () => {
    expect(bar.villagers.map((i) => i.key)).toEqual([...["sheep", "hunt", "berries", "fish", "farm", "wood", "gold", "stone"].map((s) => `villagers:${s}`), "move"]);
    expect(bar.ages.map((i) => i.label)).toEqual(["Clic en Feudal", "Clic en Castillos", "Clic en Imperial"]);
    expect(bar.build.slice(0, 5).map((i) => i.key)).toEqual(["house", "farm", "mill", "lumber_camp", "mining_camp"]);
    const ages = bar.build.map((i) => ["dark", "feudal", "castle", "imperial"].indexOf(i.age));
    expect(ages).toEqual([...ages].sort((a, b) => a - b));

    const tabs = bar.producers.map((t) => t.key);
    expect(tabs[0]).toBe("town_center");
    expect(tabs.indexOf("barracks")).toBeLessThan(tabs.indexOf("archery_range"));
    const tab = (key: string) => bar.producers.find((t) => t.key === key)!.items.map((i) => i.key);
    expect(tab("town_center")).toEqual(expect.arrayContaining(["loom", "wheelbarrow"]));
    expect(tab("town_center")).not.toContain("feudal_age");
    expect(tab("archery_range")).toEqual(expect.arrayContaining(["archer", "skirmisher"]));
    expect(bar.producers.find((t) => t.key === "archery_range")!.label).toBe("Galería de tiro");

    // Todo lo de la lista está una sola vez, y cada caja tiene su ícono.
    const keys = allItems(bar).map((i) => i.key);
    expect(new Set(keys).size).toBe(keys.length);
    expect(keys.length).toBe(8 + 1 + Object.keys(data.entities).length - 1); // sin el aldeano
    const icons = new Set(JSON.parse(readFileSync(join(__dirname, "..", "..", "..", "public", "icons", "index.json"), "utf8")) as string[]);
    for (const i of allItems(bar)) expect(icons.has(i.icon), i.key).toBe(true);
  });

  it("filtra por la civ y por el mapa", () => {
    expect(paletteBar(data, "mapuche").producers.map((t) => t.key)).not.toContain("stable");
    const arabia = paletteBar(data, "any", maps.find((m) => m.key === "arabia")!);
    expect(arabia.build.map((i) => i.key)).not.toContain("dock");
    expect(arabia.producers.map((t) => t.key)).not.toContain("dock");
    expect(arabia.villagers.map((i) => i.key)).not.toContain("villagers:fish");
  });

  it("en la Edad Oscura esconde lo de Feudal y lo que se hace en un edificio que todavía no está", () => {
    const here = paletteHere(bar, BO, 2, data);
    const shown = allItems(here.bar).map((i) => i.key);
    expect(shown).toEqual(expect.arrayContaining(["villagers:sheep", "move", "house", "barracks", "loom"]));
    expect(shown).not.toContain("archer");
    expect(shown).not.toContain("stable");
    expect(here.hidden.get("stable")).toBe("Recién después del clic en Feudal");
    expect(here.hidden.get("militia")).toMatch(/^Recién después de construir cuartel/);
    expect(here.hidden.get("feudal_age")).toBe("ya está en la BO");
    expect(here.bar.producers.map((t) => t.key)).not.toContain("archery_range");
    expect(here.bar.producers.map((t) => t.key)).toContain("town_center");
  });

  it("al final de la BO muestra lo de Feudal y dice por qué falta lo demás", () => {
    expect(unavailableHere(item("archer"), BO, BO.length, data)).toBeNull();
    expect(unavailableHere(item("man_at_arms"), BO, BO.length, data)).toBeNull();
    expect(unavailableHere(item("stable"), BO, BO.length, data)).toBeNull();
    expect(unavailableHere(item("knight"), BO, BO.length, data)).toBe("Recién después del clic en Castillos");
    expect(unavailableHere(item("castle_age"), BO, BO.length, data)).toBe("Faltan 2 edificios de Feudal en la BO (galería de tiro, establo, herrería, mercado)");
    expect(unavailableHere(item("scout_cavalry"), BO, BO.length, data)).toBe("falta un establo en la BO");
    const withStable = [...BO, step("s8", { do: "build", what: "stable", from: "wood", assign: "wood" })];
    expect(unavailableHere(item("castle_age"), withStable, withStable.length, data)).toBeNull();
    expect(unavailableHere(item("scout_cavalry"), withStable, BO.length, data)).toBe("Recién después de construir establo: ahí se hace");
  });

  it("un edificio que pide otro antes no está si ese no está (la galería sin cuartel), salvo para la civ que no lo pide", () => {
    const noBarracks = BO.filter((s) => s.id !== "s5" && s.id !== "s7");
    expect(unavailableHere(item("archery_range"), noBarracks, noBarracks.length, data)).toBe("falta un cuartel en la BO");
    expect(unavailableHere(item("archery_range"), BO, BO.length, data)).toBeNull();
    expect(unavailableHere(item("archery_range"), noBarracks, noBarracks.length, data, "khmer")).toBeNull();
  });

  it("el buscador encuentra sin tildes y por el principio de cada palabra", () => {
    const items = allItems(bar);
    expect(searchPalette(items, "galeria").map((i) => i.key)).toEqual(["archery_range"]);
    expect(searchPalette(items, "clic feu").map((i) => i.key)).toEqual(["feudal_age"]);
    expect(searchPalette(items, "aldeanos ma").map((i) => i.key)).toEqual(["villagers:wood"]);
    expect(searchPalette(items, "  ")).toEqual([]);
  });
});
