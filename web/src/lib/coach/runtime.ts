import type { AnalysisResult } from "./types";

/** El Core de C# corriendo en WebAssembly (src/BoCoach.Wasm): el mismo validador y Planner que la app. */
export interface CoachRuntime {
  /** Versión del .dat de los datos del juego cargados (por ejemplo "VER 8.9"). */
  datVersion: string;
  /** Valida, describe y simula una BO (el JSON de la app). */
  analyze(buildOrderJson: string): AnalysisResult;
}

interface CoachExports {
  BoCoach: { Wasm: { Exports: { Init(gameData: string, events: string, maps: string): string; Analyze(json: string): string } } };
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
    analyze: (json) => JSON.parse(coach.Analyze(json)) as AnalysisResult,
  };
}
