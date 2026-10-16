import { describe, expect, it } from "vitest";
import { civLabel, shareDescription, shareFacts, type ShareInfo } from "./share";
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
    expect(shareFacts({ ...scouts, civ: "britons", castle_click: 960 })).toBe("britons · Feudal 8:10 · Castillos 16:00 · por Ana");
    expect(shareFacts({ ...scouts, feudal_click: null })).toBe("Cualquier civ · por Ana");
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

  it("nombra a la civ cualquiera", () => {
    expect(civLabel("any")).toBe("Cualquier civ");
    expect(civLabel("franks")).toBe("franks");
  });
});

describe("dirección de la web", () => {
  it("usa la configurada, si no la de Vercel y si no localhost", () => {
    expect(siteUrl({ NEXT_PUBLIC_SITE_URL: "https://bo.example.com/" })).toBe("https://bo.example.com");
    expect(siteUrl({ VERCEL_PROJECT_PRODUCTION_URL: "aoe2-bo-coach.vercel.app" })).toBe("https://aoe2-bo-coach.vercel.app");
    expect(siteUrl({})).toBe("http://localhost:3000");
  });
});
