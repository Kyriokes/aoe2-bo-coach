/** Lo que el editor decide de lo que entra por el mouse y el teclado, puro. */

/**
 * Un mouse que se mueve sin ningún botón apretado: la suelta se perdió (Alt+Tab y soltar en otra ventana). En un dedo o
 * un lápiz no dice nada: `buttons` solo vale para el mouse.
 */
export function isLostRelease(e: { pointerType: string; buttons: number }) {
  return e.pointerType === "mouse" && e.buttons === 0;
}
