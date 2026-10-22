import type { Analysis, PlanBlock } from "../coach/types";
import type { GameDataLite } from "./constraints";
import type { EditorStep } from "./model";
import { blockReason } from "./text";

/**
 * Por qué espera una caja en el lugar donde cayó: el último bloqueo de sus pasos antes de arrancar. Con un bloqueo
 * por recursos se estima cuánto falta (el costo de la entidad menos el stock de la muestra más cercana).
 */
export function waitReason(analysis: Analysis, block: EditorStep[], data: GameDataLite | null): string | null {
  const ids = new Set(block.map((s) => s.id));
  const waits = (analysis.blocks ?? []).filter((b) => ids.has(b.step_id));
  if (waits.length === 0) {
    // Las esperas por "a N de población" o por "after" no dejan bloqueo: lo que hay es el aviso del validador.
    const issue = analysis.issues.find((i) => i.severity === "warning" && i.step_id !== null && ids.has(i.step_id) && /población|'after'/.test(i.message));
    return issue ? issue.message : null;
  }
  const last = waits.reduce((best, b) => (b.to > best.to ? b : best));
  return blockReason(last, missingAmount(analysis, last, block, data));
}

/** El último bloqueo de la caja, o null si espera por otra cosa (población, "after", el paso anterior). */
export function lastWait(analysis: Analysis, block: EditorStep[]): PlanBlock | null {
  const ids = new Set(block.map((s) => s.id));
  const waits = (analysis.blocks ?? []).filter((b) => ids.has(b.step_id));
  return waits.length === 0 ? null : waits.reduce((best, b) => (b.to > best.to ? b : best));
}

function missingAmount(analysis: Analysis, wait: PlanBlock, block: EditorStep[], data: GameDataLite | null): number | null {
  if (wait.reason !== "resources" || !wait.missing || !data) return null;
  const step = block.find((s) => s.id === wait.step_id);
  const cost = step?.what ? data.entities[step.what]?.cost?.[wait.missing] : undefined;
  if (cost === undefined) return null;
  const samples = analysis.timeline ?? [];
  if (samples.length === 0) return null;
  const nearest = samples.reduce((best, s) => (Math.abs(s.time - wait.from) < Math.abs(best.time - wait.from) ? s : best));
  const units = step?.do === "train" || step?.do === "build" ? Math.max(1, step.count) : 1;
  return Math.max(0, Math.round(cost * units - nearest[wait.missing]));
}
