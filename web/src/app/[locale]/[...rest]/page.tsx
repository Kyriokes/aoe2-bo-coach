import { notFound } from "next/navigation";

/**
 * Lo que no calza con ninguna página: el 404 con la cabecera y el pie del idioma. El estado lo pone el proxy (ver
 * i18n/routing.ts), que manda acá lo que no es una página de la web.
 */
export default function CatchAll() {
  notFound();
}
