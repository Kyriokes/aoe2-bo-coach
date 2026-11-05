/** Copia un texto. El navegador puede no dejar (sin permiso, o fuera de https): dice si se pudo. */
export async function copyText(text: string) {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    return false;
  }
}
