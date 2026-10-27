import { loadCoach } from "./browser";
import type { AnalysisResult } from "./types";

/**
 * D12 (e): `Analyze` para el modo asistido, que tarda unos 400 ms (generar con el intérprete de .NET en WebAssembly).
 * Primero deja pintar la página ("calculando…") y después corre en el hilo de la página. La spec pide un Web Worker
 * si no llega a 300 ms: se probó y `dotnet.create()` no termina dentro de un worker de módulo (ni con `self.window`),
 * así que queda pendiente (PLAN.md, D12). Solo desde componentes de cliente.
 */
export async function analyzeAsync(json: string): Promise<AnalysisResult> {
  const coach = await loadCoach();
  await new Promise((resolve) => setTimeout(resolve, 0));
  return coach.analyze(json);
}
