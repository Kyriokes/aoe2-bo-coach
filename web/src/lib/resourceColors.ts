/**
 * Los colores de los recursos como en el HUD del juego (los mismos que el mod de la app: comida roja, madera marrón,
 * oro amarillo, piedra gris y población azul). Las variables están en globals.css; los nombres, en el diccionario (sim).
 */
export const RESOURCES = [
  { key: "food", color: "var(--food)" },
  { key: "wood", color: "var(--wood)" },
  { key: "gold", color: "var(--gold)" },
  { key: "stone", color: "var(--stone)" },
] as const;

export const POPULATION = { color: "var(--pop)" } as const;
