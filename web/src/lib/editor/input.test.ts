import { describe, expect, it } from "vitest";
import { isLostRelease } from "./input";

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
