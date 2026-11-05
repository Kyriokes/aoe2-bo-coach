import { describe, expect, it } from "vitest";
import { isLostRelease, parseStepNumber } from "./input";

describe("lo escrito en un campo de número", () => {
  it("vacío es sin valor, y lo demás un entero entre el mínimo y 200", () => {
    expect(parseStepNumber("")).toBeUndefined();
    expect(parseStepNumber("  ")).toBeUndefined();
    expect(parseStepNumber("15")).toBe(15);
    expect(parseStepNumber("1.6")).toBe(2);
    expect(parseStepNumber("0")).toBe(1);
    expect(parseStepNumber("-3")).toBe(1);
    expect(parseStepNumber("0", 0)).toBe(0);
    expect(parseStepNumber("999")).toBe(200);
  });

  it("lo que no es un número no llega al editor", () => {
    expect(parseStepNumber("abc")).toBeUndefined();
    expect(parseStepNumber("1e999")).toBeUndefined();
  });
});

describe("la suelta perdida de un arrastre", () => {
  it("un mouse que se mueve sin botón ya no está arrastrando", () => {
    expect(isLostRelease({ pointerType: "mouse", buttons: 0 })).toBe(true);
    expect(isLostRelease({ pointerType: "mouse", buttons: 1 })).toBe(false);
  });

  it("en un dedo o un lápiz no se puede saber así", () => {
    expect(isLostRelease({ pointerType: "touch", buttons: 0 })).toBe(false);
    expect(isLostRelease({ pointerType: "pen", buttons: 0 })).toBe(false);
  });
});
