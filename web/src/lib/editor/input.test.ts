import { afterEach, describe, expect, it, vi } from "vitest";
import { boxKeyAction, holdFocus, isLostRelease, parseStepNumber } from "./input";

describe("las teclas sobre una caja", () => {
  const key = (name: string, altKey = false) => ({ key: name, altKey });

  it("Alt+← y Alt+→ la mueven; Alt+↑ y Alt+↓ cambian la cantidad si se puede; Supr la borra; Enter la elige", () => {
    expect(boxKeyAction(key("ArrowLeft", true), true, false)).toEqual({ kind: "nudge", direction: -1 });
    expect(boxKeyAction(key("ArrowRight", true), false, false)).toEqual({ kind: "nudge", direction: 1 });
    expect(boxKeyAction(key("ArrowUp", true), true, false)).toEqual({ kind: "count", delta: 1 });
    expect(boxKeyAction(key("ArrowDown", true), true, false)).toEqual({ kind: "count", delta: -1 });
    expect(boxKeyAction(key("ArrowUp", true), false, false)).toBeNull();
    expect(boxKeyAction(key("Delete"), true, false)).toEqual({ kind: "remove" });
    expect(boxKeyAction(key("Backspace"), true, false)).toEqual({ kind: "remove" });
    expect(boxKeyAction(key("Enter"), true, false)).toEqual({ kind: "select" });
    expect(boxKeyAction(key(" "), true, false)).toEqual({ kind: "select" });
    expect(boxKeyAction(key("a"), true, false)).toBeNull();
    expect(boxKeyAction(key("ArrowLeft"), true, false)).toBeNull();
  });

  // La caja elegía y cancelaba el Enter, y el botón − o + no hacía nada.
  it("con el foco en el − o el + de adentro, Enter y espacio son del botón; lo demás sigue siendo de la caja", () => {
    expect(boxKeyAction(key("Enter"), true, true)).toBeNull();
    expect(boxKeyAction(key(" "), true, true)).toBeNull();
    expect(boxKeyAction(key("ArrowLeft", true), true, true)).toEqual({ kind: "nudge", direction: -1 });
    expect(boxKeyAction(key("Delete"), true, true)).toEqual({ kind: "remove" });
  });
});

describe("el foco de la hoja del celular", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  // Declaraba aria-modal pero el foco se quedaba afuera: Escape no le llegaba y no se cerraba con el teclado.
  it("se lo lleva al abrir y lo devuelve al cerrar", () => {
    const opener = { focus: vi.fn() };
    const sheet = { focus: vi.fn() };
    vi.stubGlobal("document", { activeElement: opener });

    const close = holdFocus(sheet as unknown as HTMLElement);
    expect(sheet.focus).toHaveBeenCalledOnce();
    expect(opener.focus).not.toHaveBeenCalled();

    close?.();
    expect(opener.focus).toHaveBeenCalledOnce();
  });

  it("sin nada enfocado antes, cerrar no falla", () => {
    vi.stubGlobal("document", { activeElement: null });
    expect(() => holdFocus({ focus: () => {} } as unknown as HTMLElement)?.()).not.toThrow();
    expect(holdFocus(null)).toBeUndefined();
  });
});

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
