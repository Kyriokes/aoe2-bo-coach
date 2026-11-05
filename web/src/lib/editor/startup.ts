/**
 * Qué carga el editor cuando corre su efecto de arranque. Con Cache Components, Next deja las últimas rutas en
 * `<Activity hidden>`: al volver a `/subir` los efectos se crean de nuevo aunque el estado siga ahí, así que el efecto
 * no puede cargar otra vez lo que ya cargó (borraría lo que se armó). `loaded` es el `initialJson` de la última carga,
 * null si todavía no se cargó nada o si después de guardar hay que empezar de nuevo.
 */
export function whatToLoad(loaded: string | null, initialJson: string): "nothing" | "initial" {
  return loaded === initialJson ? "nothing" : "initial";
}
