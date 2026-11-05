import { unstable_rethrow } from "next/navigation";

/** Lo que devuelven las acciones de servidor (como `ActionResult` de app/actions.ts). */
type Result<T> = { ok: true; value: T } | { ok: false; error: string };

/**
 * Llama a una acción de servidor desde un componente sin que su rechazo llegue a React. La promesa rechaza si se corta
 * la red, si hay un deploy nuevo con la pestaña abierta o si el cuerpo pasa el tope de Next, y dentro de startTransition
 * eso lo toma error.tsx: se pierde la página, y en el editor, la BO. Lo que Next usa para navegar (redirect) sigue de largo.
 */
export async function safeAction<T>(action: () => Promise<Result<T>>, message: string): Promise<Result<T>> {
  try {
    return await action();
  } catch (error) {
    unstable_rethrow(error);
    console.error(error);
    return { ok: false, error: message };
  }
}
