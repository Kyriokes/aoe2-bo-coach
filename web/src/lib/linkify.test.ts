import { describe, expect, it } from "vitest";
import { linkify } from "./linkify";

describe("linkify", () => {
  it("separa la fuente de una BO importada en un enlace, sin el punto final", () => {
    expect(linkify("Arqueros a 23. Fuente: https://www.buildorderguide.com/builds/abc123.")).toEqual([
      { text: "Arqueros a 23. Fuente: " },
      { text: "https://www.buildorderguide.com/builds/abc123", href: "https://www.buildorderguide.com/builds/abc123" },
      { text: "." },
    ]);
  });

  it("deja el texto sin enlaces como está", () => {
    expect(linkify("Sin fuente.")).toEqual([{ text: "Sin fuente." }]);
    expect(linkify("")).toEqual([]);
  });

  it("encuentra varios y no se come el paréntesis ni la coma de afuera", () => {
    expect(linkify("(ver https://a.com/x), y http://b.org")).toEqual([
      { text: "(ver " },
      { text: "https://a.com/x", href: "https://a.com/x" },
      { text: "), y " },
      { text: "http://b.org", href: "http://b.org" },
    ]);
  });

  it("no hace enlace de lo que no es http ni https", () => {
    for (const text of ["javascript:alert(1)", "data:text/html,hola", "ftp://a.com", "https://", "www.a.com"]) {
      expect(linkify(text).every((part) => part.href === undefined)).toBe(true);
    }
  });
});
