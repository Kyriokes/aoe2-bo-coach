"use client";

import { useSyncExternalStore } from "react";

/** Lo mismo que `sm:` de Tailwind: debajo de 640 px es un celular (lista en vez de arrastrar, paleta en una hoja). */
const QUERY = "(max-width: 639px)";

function subscribe(onChange: () => void) {
  const media = window.matchMedia(QUERY);
  media.addEventListener("change", onChange);
  return () => media.removeEventListener("change", onChange);
}

/** Si la pantalla es angosta (en el servidor, no: la primera pintada es la de PC y se corrige al hidratar). */
export function useNarrow() {
  return useSyncExternalStore(
    subscribe,
    () => window.matchMedia(QUERY).matches,
    () => false,
  );
}
