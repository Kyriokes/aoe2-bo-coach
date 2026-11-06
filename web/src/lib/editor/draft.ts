import type { Visibility } from "../buildOrder";
import { toJson, type EditorState } from "./model";

/**
 * El borrador del editor: lo que se está armando y todavía no se guardó. Va en sessionStorage: es de esta pestaña,
 * sobrevive a recargar, a iniciar sesión (se sale del sitio y se vuelve) y a cambiar de idioma, y se va con la pestaña.
 * El navegador puede no dejar usarlo (modo privado, sin lugar): sin borrador, el editor anda igual.
 */
export interface EditorDraft {
  /** La BO como se la manda al Core (`toJson`). */
  json: string;
  visibility: Visibility;
}

type DraftStorage = Pick<Storage, "getItem" | "setItem" | "removeItem" | "key" | "length">;

const VERSION = 1;
const PREFIX = "bocoach.borrador.";

/** La BO nueva y cada una que se edita tienen su borrador. */
export function draftKey(editCode?: string) {
  return `${PREFIX}${editCode ?? "nueva"}`;
}

export function draftStorage(): DraftStorage | null {
  try {
    return window.sessionStorage;
  } catch {
    return null;
  }
}

/** Una BO asistida guarda sus objetivos: los pasos se generan de nuevo al abrirla (sin avisar que se recalculó). */
export function draftJson(state: Pick<EditorState, "bo" | "steps">) {
  return JSON.stringify(toJson({ bo: state.bo, steps: state.bo.assisted === undefined ? state.steps : [] }));
}

export function writeDraft(storage: DraftStorage | null, key: string, draft: EditorDraft) {
  try {
    storage?.setItem(key, JSON.stringify({ version: VERSION, ...draft }));
  } catch {
    // Sin lugar o sin permiso: queda sin borrador.
  }
}

export function readDraft(storage: DraftStorage | null, key: string): EditorDraft | null {
  try {
    const raw = storage?.getItem(key);
    if (!raw) return null;
    const value = JSON.parse(raw) as { version?: unknown; json?: unknown; visibility?: unknown };
    if (value.version !== VERSION || typeof value.json !== "string") return null;
    return { json: value.json, visibility: value.visibility === "public" ? "public" : "private" };
  } catch {
    return null;
  }
}

export function clearDraft(storage: DraftStorage | null, key: string) {
  try {
    storage?.removeItem(key);
  } catch {
    // Sin permiso: no hay nada que borrar.
  }
}

/** Los borradores que hay en esta pestaña, de cualquier BO. */
function hasDrafts(storage: DraftStorage | null) {
  try {
    for (let i = 0; i < (storage?.length ?? 0); i++) {
      const key = storage!.key(i);
      if (key?.startsWith(PREFIX) && readDraft(storage, key) !== null) return true;
    }
  } catch {
    // Sin permiso: no hay ninguno.
  }
  return false;
}

/**
 * Si salir de la página pierde algo. `unsaved`: las BO con cambios sin guardar según el editor. Lo que no quedó en un
 * borrador (el navegador no dejó escribirlo) se pierde siempre. Los borradores son de la pestaña: cerrarla se los lleva
 * (recargar no, pero el navegador no distingue y pregunta igual); salir para volver a ella (`comingBack`: el login, el
 * idioma o un formulario del sitio), no.
 */
export function losesWork(storage: DraftStorage | null, unsaved: Iterable<string>, comingBack: boolean) {
  for (const key of unsaved) if (readDraft(storage, key) === null) return true;
  return !comingBack && hasDrafts(storage);
}

/** Lo que el editor marcó sin guardar. Queda aunque /subir se esconda o se desmonte: el trabajo sigue en el borrador. */
const unsaved = new Set<string>();
let comingBack = false;

export function markUnsaved(key: string, dirty: boolean) {
  if (dirty) unsaved.add(key);
  else unsaved.delete(key);
}

/** La próxima salida vuelve a esta pestaña (el login, el idioma, una búsqueda): el borrador la sobrevive. `false` si al final no se sale. */
export function leaveAndComeBack(value = true) {
  comingBack = value;
}

/**
 * Un clic en un enlace que sale y vuelve a esta pestaña, en la misma página: el idioma (/idioma guarda la elección y
 * redirige de vuelta, y el editor recupera el borrador solo). Con Ctrl, Mayús, Cmd o Alt el navegador lo abre en otra
 * pestaña o ventana, o lo descarga: esta no se va, y no se toca nada.
 */
export function comeBackOnClick(event: Pick<MouseEvent, "button" | "ctrlKey" | "metaKey" | "shiftKey" | "altKey">) {
  if (event.button === 0 && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey) leaveAndComeBack();
}

type Attributes = { getAttribute(name: string): string | null };

/**
 * Un formulario que se manda: si el navegador carga otra página del sitio en esta pestaña (buscar en la biblioteca, "Ver
 * cruce"), sale y vuelve a ella con el borrador intacto. No, si lo manejó la página (una acción del servidor, un onSubmit),
 * si va a otra pestaña o ventana, a otro sitio, o por POST. El botón que lo manda puede cambiar el método, el destino y a
 * dónde va (`formmethod`, `formtarget`, `formaction`). `here`: la dirección de esta página, a la que va sin `action`.
 */
export function comeBackOnSubmit(event: { defaultPrevented: boolean; form: Attributes; submitter: Attributes | null }, here: string) {
  if (event.defaultPrevented) return;
  const attribute = (name: string) => event.submitter?.getAttribute(`form${name}`) ?? event.form.getAttribute(name);
  // Un método que no se entiende es GET, como en el navegador.
  const method = attribute("method")?.toLowerCase();
  if (method === "post" || method === "dialog") return;
  const target = attribute("target")?.toLowerCase() ?? "";
  if (target !== "" && target !== "_self") return;
  try {
    if (new URL(attribute("action") || here, here).origin === new URL(here).origin) leaveAndComeBack();
  } catch {
    // Una dirección que no se entiende: el navegador no va a ningún lado.
  }
}

let toApp = false;

/**
 * Lo que navega ahora abre la app (bocoach://) y la página se queda: no hay nada que perder. Chrome igual dispara
 * beforeunload, con un enlace y con location.href a ese esquema, y con Cancelar en "¿Salir del sitio?" la app no se abre.
 * Chrome lo dispara en el momento; vale para esta tarea, así no queda prendido si el navegador no lo dispara (no abrió
 * nada, o lo abrió en otra pestaña).
 */
export function openingApp() {
  toApp = true;
  setTimeout(() => (toApp = false), 0);
}

/**
 * El aviso del navegador antes de salir. Es uno solo para toda la pestaña y no depende de que el editor esté a la vista:
 * con Cache Components, Next esconde /subir en `<Activity hidden>` al ir a otra página y React corre los cleanups, así
 * que un aviso atado al editor se iba y cerrar la pestaña desde la biblioteca perdía la BO sin preguntar.
 */
export function warnBeforeUnload(event: BeforeUnloadEvent) {
  const loses = !toApp && losesWork(draftStorage(), unsaved, comingBack);
  comingBack = false;
  toApp = false;
  if (!loses) return;
  event.preventDefault();
  event.returnValue = ""; // los navegadores viejos miran esto
}
