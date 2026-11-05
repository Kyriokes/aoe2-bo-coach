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

type DraftStorage = Pick<Storage, "getItem" | "setItem" | "removeItem">;

const VERSION = 1;

/** La BO nueva y cada una que se edita tienen su borrador. */
export function draftKey(editCode?: string) {
  return `bocoach.borrador.${editCode ?? "nueva"}`;
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
