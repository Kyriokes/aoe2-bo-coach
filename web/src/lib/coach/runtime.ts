import type { Locale } from "@/i18n/locales";
import type { AnalysisResult, ImportResult } from "./types";

/** El Core de C# corriendo en WebAssembly (src/BoCoach.Wasm): el mismo validador y Planner que la app. */
export interface CoachRuntime {
  /** Versión del .dat de los datos del juego cargados (por ejemplo "VER 8.9"). */
  datVersion: string;
  /** Valida, describe y simula una BO (el JSON de la app). Los pasos y los avisos salen en `locale`. */
  analyze(buildOrderJson: string, locale?: Locale): AnalysisResult;
  /** Reconoce el formato (BO de la app, guía de RTS Overlay o de buildorderguide.com) y la convierte. */
  importBuildOrder(json: string): ImportResult;
}

interface CoachExports {
  BoCoach: { Wasm: { Exports: { Init(gameData: string, events: string, maps: string): string; Analyze(json: string, language: string): string; Import(json: string): string } } };
}

interface DotnetModule {
  dotnet: {
    create(): Promise<{ getAssemblyExports(assembly: string): Promise<unknown> }>;
  };
}

/** Arranca el runtime de .NET (dotnet.js) y le carga game_data, events.json y maps.json. */
export async function startCoach(dotnetModule: DotnetModule, gameDataJson: string, eventsJson: string, mapsJson = ""): Promise<CoachRuntime> {
  const runtime = await dotnetModule.dotnet.create();
  const exports = (await runtime.getAssemblyExports("BoCoach.Wasm")) as CoachExports;
  const coach = exports.BoCoach.Wasm.Exports;
  const datVersion = coach.Init(gameDataJson, eventsJson, mapsJson);
  return {
    datVersion,
    analyze: (json, locale = "es") => JSON.parse(coach.Analyze(json, locale)) as AnalysisResult,
    importBuildOrder: (json) => JSON.parse(coach.Import(json)) as ImportResult,
  };
}
