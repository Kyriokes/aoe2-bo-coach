import type { PlanBlock } from "./types";

/**
 * Los trabones de cada paso, como los muestra la página de una BO ("Se traba de ... a ..."): de 5 s o más y sin las
 * esperas de algo que ya viene en camino (esperar la edad que se investiga no es trabarse).
 */
export function stallsByStep(blocks: readonly PlanBlock[]): Map<string, PlanBlock[]> {
  const stalls = new Map<string, PlanBlock[]>();
  for (const block of blocks) {
    if (block.expected || block.to - block.from < 5) continue;
    stalls.set(block.step_id, [...(stalls.get(block.step_id) ?? []), block]);
  }
  return stalls;
}
