import { COACH_BASE } from "./browser";

/** Las BO de ejemplo que trae la app (las copia scripts/build-coach.mjs a public/coach/ejemplos). */
export interface Example {
  file: string;
  name: string;
}

async function get(file: string) {
  const response = await fetch(`${COACH_BASE}/ejemplos/${file}`);
  if (!response.ok) throw new Error(`HTTP ${response.status}`);
  return response;
}

export async function loadExamples() {
  return (await (await get("index.json")).json()) as Example[];
}

/** El JSON de un ejemplo. Rechaza si no se pudo traer. */
export async function loadExample(file: string) {
  return (await get(file)).text();
}
