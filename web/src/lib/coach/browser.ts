import { startCoach, type CoachRuntime } from "./runtime";

/** Dónde quedan el runtime y los datos (los arma scripts/build-coach.mjs). */
export const COACH_BASE = "/coach";

let loading: Promise<CoachRuntime> | null = null;

/**
 * Carga el Core en el navegador la primera vez que se pide (unos 2 MB comprimidos) y lo reusa después.
 * Solo se puede llamar desde componentes de cliente.
 */
export function loadCoach(): Promise<CoachRuntime> {
  loading ??= (async () => {
    const [gameData, events, maps] = await Promise.all([
      fetch(`${COACH_BASE}/game_data.json`).then((r) => r.text()),
      fetch(`${COACH_BASE}/events.json`).then((r) => r.text()),
      fetch(`${COACH_BASE}/maps.json`).then((r) => (r.ok ? r.text() : "")),
    ]);
    const url = `${COACH_BASE}/_framework/dotnet.js`;
    const dotnetModule = await import(/* webpackIgnore: true */ /* turbopackIgnore: true */ url);
    return startCoach(dotnetModule, gameData, events, maps);
  })().catch((error) => {
    loading = null; // para poder reintentar
    throw error;
  });
  return loading;
}
