/**
 * Datos estructurados (schema.org en JSON-LD) para que los buscadores entiendan qué es cada página. Van en un
 * <script type="application/ld+json">; el "<" se escapa porque el nombre o la descripción de una BO los escribe
 * cualquiera, y un "</script>" adentro cerraría la etiqueta (la guía de JSON-LD de Next recomienda lo mismo).
 */
export function jsonLdText(data: unknown): string {
  return JSON.stringify(data).replace(/</g, "\\u003c");
}

/** El juego, para el "about" de las páginas. */
export const GAME = { "@type": "VideoGame", name: "Age of Empires II: Definitive Edition" } as const;
