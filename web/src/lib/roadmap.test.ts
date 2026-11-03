import { describe, expect, it } from "vitest";
import { LOCALES } from "@/i18n/locales";
import { roadmapContent } from "./roadmap";

describe("el roadmap", () => {
  it.each(LOCALES.map((l) => [l]))("%s tiene la forma que espera la página y una fecha válida", (locale) => {
    const content = roadmapContent(locale);
    expect(content.title).not.toBe("");
    expect(content.description).not.toBe("");
    expect(content.intro.length).toBeGreaterThan(0);
    expect(content.closing).not.toBe("");
    expect(content.updated).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    expect(Number.isNaN(new Date(content.updated).getTime())).toBe(false);
    expect(content.sections.length).toBeGreaterThan(0);
    for (const section of content.sections) {
      expect(section.items.length, section.id).toBeGreaterThan(0);
      for (const item of section.items) {
        expect(item.title, section.id).not.toBe("");
        expect(item.text, item.title).not.toBe("");
      }
    }
  });

  it("todos los idiomas cuentan lo mismo: las mismas secciones y la misma cantidad de items", () => {
    const shape = (locale: (typeof LOCALES)[number]) => roadmapContent(locale).sections.map((s) => s.items.length);
    for (const locale of LOCALES) {
      expect(shape(locale), locale).toEqual(shape("es"));
      expect(roadmapContent(locale).updated, locale).toBe(roadmapContent("es").updated);
    }
  });

  it("sin guiones largos", () => {
    for (const locale of LOCALES) expect(JSON.stringify(roadmapContent(locale)).includes("\u2014"), locale).toBe(false);
  });
});
