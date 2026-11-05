/** Lo que el editor decide de lo que entra por el mouse y el teclado, puro. */

export type BoxKeyAction = { kind: "nudge"; direction: -1 | 1 } | { kind: "count"; delta: 1 | -1 } | { kind: "remove" } | { kind: "select" };

/**
 * Lo que hace una tecla sobre una caja de la línea. `inner`: el foco está en un botón de adentro (− o +); Enter y espacio
 * son de ese botón (si la caja los toma y los cancela, el botón no se activa).
 */
export function boxKeyAction(e: { key: string; altKey: boolean }, countEditable: boolean, inner: boolean): BoxKeyAction | null {
  if (e.altKey && (e.key === "ArrowLeft" || e.key === "ArrowRight")) return { kind: "nudge", direction: e.key === "ArrowLeft" ? -1 : 1 };
  if (e.altKey && countEditable && (e.key === "ArrowUp" || e.key === "ArrowDown")) return { kind: "count", delta: e.key === "ArrowUp" ? 1 : -1 };
  if (e.key === "Delete" || e.key === "Backspace") return { kind: "remove" };
  if (!inner && (e.key === "Enter" || e.key === " ")) return { kind: "select" };
  return null;
}

/** Ref de una hoja modal: se lleva el foco al abrir (si no, las teclas, como Escape, no le llegan) y lo devuelve al cerrar. */
export function holdFocus(element: HTMLElement | null) {
  if (!element) return;
  const previous = document.activeElement as HTMLElement | null;
  element.focus();
  return () => previous?.focus();
}

/** Lo escrito en un campo de número del paso: vacío (o lo que no es un número) es sin valor; si no, un entero entre `min` y 200. */
export function parseStepNumber(text: string, min = 1): number | undefined {
  const value = Number(text);
  if (text.trim() === "" || !Number.isFinite(value)) return undefined;
  return Math.max(min, Math.min(200, Math.round(value)));
}

/**
 * Un mouse que se mueve sin ningún botón apretado: la suelta se perdió (Alt+Tab y soltar en otra ventana). En un dedo o
 * un lápiz no dice nada: `buttons` solo vale para el mouse.
 */
export function isLostRelease(e: { pointerType: string; buttons: number }) {
  return e.pointerType === "mouse" && e.buttons === 0;
}
