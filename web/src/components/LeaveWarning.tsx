"use client";

import { useEffect } from "react";
import { warnBeforeUnload } from "@/lib/editor/draft";

/**
 * El aviso del navegador antes de cerrar la pestaña con una BO sin guardar, en cada documento del sitio: el borrador es de
 * la pestaña y sigue ahí después de recargar, de volver del login o de cambiar de idioma, en una página donde el editor
 * nunca se montó. Se pone una vez y no se saca (Next esconde las rutas en `<Activity hidden>` y corre los cleanups): mira
 * los borradores y lo que marcó el editor, no qué página está a la vista.
 */
export function LeaveWarning() {
  useEffect(() => {
    window.addEventListener("beforeunload", warnBeforeUnload);
  }, []);
  return null;
}
