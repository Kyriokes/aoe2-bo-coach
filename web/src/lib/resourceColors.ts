/**
 * Los colores de los recursos como en el HUD del juego (los mismos que el mod de la app: comida roja, madera marrón,
 * oro amarillo, piedra gris y población azul). Las variables están en globals.css.
 */
export const RESOURCES = [
  { key: "food", label: "Comida", color: "var(--food)" },
  { key: "wood", label: "Madera", color: "var(--wood)" },
  { key: "gold", label: "Oro", color: "var(--gold)" },
  { key: "stone", label: "Piedra", color: "var(--stone)" },
] as const;

export const POPULATION = { label: "Población", color: "var(--pop)" } as const;
