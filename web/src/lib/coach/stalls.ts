import type { PlanBlock } from "./types";

/** "stall": la BO se traba (faltan casas, el edificio, aldeanos). "wait": espera a juntar recursos. */
export interface BlockLine {
  kind: "stall" | "wait";
  block: PlanBlock;
}

/**
 * Los renglones de bloqueo de cada paso, como los muestra la página de una BO. Un trabón de 5 s o más es "Se traba";
 * juntar recursos es una espera y se muestra si dura 30 s o más (esperar unos segundos la madera de un molino es lo
 * normal en una partida). Las esperas de algo que ya viene en camino no se muestran: esperar la edad que se investiga
 * no es trabarse.
 */
export function blockLines(blocks: readonly PlanBlock[]): Map<string, BlockLine[]> {
  const lines = new Map<string, BlockLine[]>();
  for (const block of blocks) {
    if (block.expected) continue;
    const kind = block.reason === "resources" ? "wait" : "stall";
    if (block.to - block.from < (kind === "wait" ? 30 : 5)) continue;
    lines.set(block.step_id, [...(lines.get(block.step_id) ?? []), { kind, block }]);
  }
  return lines;
}
