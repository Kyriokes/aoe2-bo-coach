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
 * (recargar no, pero el navegador no distingue y pregunta igual); salir del sitio para volver a ella (`comingBack`, el
 * login), no.
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

/** La próxima salida del sitio vuelve a esta pestaña (el login): el borrador la sobrevive. `false` si al final no se sale. */
export function leaveAndComeBack(value = true) {
  comingBack = value;
}

/**
 * El aviso del navegador antes de salir. Es uno solo para toda la pestaña y no depende de que el editor esté a la vista:
 * con Cache Components, Next esconde /subir en `<Activity hidden>` al ir a otra página y React corre los cleanups, así
 * que un aviso atado al editor se iba y cerrar la pestaña desde la biblioteca perdía la BO sin preguntar.
 */
export function warnBeforeUnload(event: BeforeUnloadEvent) {
  const loses = losesWork(draftStorage(), unsaved, comingBack);
  comingBack = false;
  if (!loses) return;
  event.preventDefault();
  event.returnValue = ""; // los navegadores viejos miran esto
}
