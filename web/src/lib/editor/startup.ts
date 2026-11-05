/**
 * Qué carga el editor cuando corre su efecto de arranque. Con Cache Components, Next deja las últimas rutas en
 * `<Activity hidden>`: al volver a `/subir` los efectos se crean de nuevo aunque el estado siga ahí, así que el efecto
 * no puede cargar otra vez lo que ya cargó (borraría lo que se armó). `loaded` es el `initialJson` de la última carga,
 * null si todavía no se cargó nada o si después de guardar hay que empezar de nuevo. Al abrir, un borrador le gana a lo
 * que manda la página; después, una versión nueva que manda el servidor no pisa los cambios sin guardar.
 */
export function whatToLoad(loaded: string | null, initialJson: string, dirty = false, hasDraft = false): "nothing" | "draft" | "initial" {
  if (loaded === null) return hasDraft ? "draft" : "initial";
  return loaded === initialJson || dirty ? "nothing" : "initial";
}
