import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { CIV_NAMES } from "./civNames";
import { byline, civLabel, shareDescription, shareFacts, type ShareInfo } from "./share";
import { siteUrl } from "./site";

const scouts: ShareInfo = {
  name: "Scouts Feudal",
  civ: "any",
  author: "Ana",
  description: null,
  feudal_click: 490,
  castle_click: null,
};

describe("vista previa al compartir", () => {
  it("resume civ, clics de edad y autor", () => {
    expect(shareFacts(scouts)).toBe("Cualquier civ · Feudal 8:10 · por Ana");
    expect(shareFacts({ ...scouts, civ: "britons", castle_click: 960 })).toBe("Britanos · Feudal 8:10 · Castillos 16:00 · por Ana");
    expect(shareFacts({ ...scouts, feudal_click: null })).toBe("Cualquier civ · por Ana");
  });

  it("si la guía es de otro, dice también quién la subió", () => {
    expect(byline({ author: "Hera", uploader: "Ana" })).toBe("Hera, subida por Ana");
    expect(byline({ author: "Ana", uploader: "Ana" })).toBe("Ana");
    expect(byline({ author: "ana ", uploader: "Ana" })).toBe("ana ");
    expect(byline({ author: "Ana" })).toBe("Ana");
    expect(shareFacts({ ...scouts, author: "Hera", uploader: "Ana" })).toBe("Cualquier civ · Feudal 8:10 · por Hera, subida por Ana");
  });

  it("suma la descripción en un renglón y la corta si es larga", () => {
    expect(shareDescription({ ...scouts, description: "Para Arabia.\n\nSin  muros." })).toBe(
      "Cualquier civ · Feudal 8:10 · por Ana. Para Arabia. Sin muros.",
    );
    const long = shareDescription({ ...scouts, description: "x".repeat(500) }, 80);
    expect(long).toHaveLength(80);
    expect(long.endsWith("…")).toBe(true);
    expect(shareDescription({ ...scouts, description: "   " })).toBe(shareFacts(scouts));
  });

  it("nombra a la civ en castellano, como la app", () => {
    expect(civLabel("any")).toBe("Cualquier civ");
    expect(civLabel("franks")).toBe("Francos");
    expect(civLabel("Hindustanis")).toBe("Indostaníes");
    expect(civLabel("atlantes")).toBe("atlantes");
    expect(civLabel("constructor")).toBe("constructor");
  });

  it("tiene el nombre de todas las civs de los datos del juego", () => {
    const dir = join(__dirname, "..", "..", "..", "data", "game_data");
    const latest = readdirSync(dir)
      .filter((f) => /^v\d+(\.\d+)*\.json$/.test(f))
      .sort((a, b) => b.localeCompare(a, undefined, { numeric: true }))[0];
    const civs = (JSON.parse(readFileSync(join(dir, latest), "utf8")) as { civs: Record<string, { names: { mx: string } }> }).civs;
    expect(CIV_NAMES).toEqual(Object.fromEntries(Object.entries(civs).map(([key, civ]) => [key, civ.names.mx])));
  });
});

describe("dirección de la web", () => {
  it("usa la configurada, si no la de Vercel y si no localhost", () => {
    expect(siteUrl({ NEXT_PUBLIC_SITE_URL: "https://bo.example.com/" })).toBe("https://bo.example.com");
    expect(siteUrl({ VERCEL_PROJECT_PRODUCTION_URL: "aoe2-bo-coach.vercel.app" })).toBe("https://aoe2-bo-coach.vercel.app");
    expect(siteUrl({})).toBe("http://localhost:3000");
  });
});
