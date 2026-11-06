"use client";

import { useEffect } from "react";
import { comeBackOnSubmit, warnBeforeUnload } from "@/lib/editor/draft";

/**
 * Los formularios GET nativos del sitio (buscar en la biblioteca, "Ver cruce") cargan otra página en esta pestaña. Se mira
 * al final de la burbuja, en window, así ya pasaron los de React (que van en el documento) y `defaultPrevented` dice si la
 * página lo manejó.
 */
function onSubmit(event: SubmitEvent) {
  if (event.target instanceof HTMLFormElement) {
    comeBackOnSubmit({ defaultPrevented: event.defaultPrevented, form: event.target, submitter: event.submitter }, window.location.href);
  }
}

/**
 * El aviso del navegador antes de cerrar la pestaña con una BO sin guardar, en cada documento del sitio: el borrador es de
 * la pestaña y sigue ahí después de recargar, de volver del login o de cambiar de idioma, en una página donde el editor
 * nunca se montó. Se pone una vez y no se saca (Next esconde las rutas en `<Activity hidden>` y corre los cleanups): mira
 * los borradores y lo que marcó el editor, no qué página está a la vista.
 */
export function LeaveWarning() {
  useEffect(() => {
    window.addEventListener("beforeunload", warnBeforeUnload);
    window.addEventListener("submit", onSubmit);
  }, []);
  return null;
}
