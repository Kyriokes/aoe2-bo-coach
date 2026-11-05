/** Lo que el editor decide de lo que entra por el mouse y el teclado, puro. */

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
