import { beforeAll, describe, expect, it } from "vitest";
import type { CoachRuntime } from "../coach/runtime";
import { startTestCoach } from "../coach/testing";
import type { Analysis } from "../coach/types";
import { SOURCES, toJson, type EditorStep } from "./model";
import { STANDARD_START } from "./opening";
import {
  chainLabel,
  detachNewVillager,
  guideLabels,
  insertByNewVillager,
  insertNewStep,
  isBuiltByNewVillager,
  newVillagerBuilds,
  populationAfter,
  removeNewVillagerBuild,
  syncNewVillagerBuilds,
  villagerNumbers,
} from "./villagers";

const step = (id: string, fields: Partial<EditorStep> & Pick<EditorStep, "do">): EditorStep => ({ id, count: 1, after: [], withPrevious: false, ...fields });

/** Los 3 iniciales hacen 2 casas y van a las ovejas; 3 más a ovejas (6 en total) y 4 a madera. */
const BO: EditorStep[] = [
  step("s1", { do: "build", what: "house", count: 2, builders: 3, assign: "sheep" }),
  step("s2", { do: "train", what: "villager", count: 3, assign: "sheep" }),
  step("s3", { do: "train", what: "villager", count: 4, assign: "wood" }),
];
const camp = step("s9", { do: "build", what: "lumber_camp", from: "sheep", assign: "sheep" });
const start = STANDARD_START;

describe("el editor por aldeano", () => {
  it("numera los aldeanos como las guías y cuenta la población", () => {
    expect(villagerNumbers(BO, start).get("s2")).toEqual({ first: 4, last: 6 });
    expect(villagerNumbers(BO, start).get("s3")).toEqual({ first: 7, last: 10 });
    expect(populationAfter(BO, 2, start)).toBe(7); // 6 aldeanos y el explorador
  });

  it("el campamento entre las ovejas y la madera lo hace el aldeano 7, que sale de los 4 de madera", () => {
    const r = insertByNewVillager(BO, camp, 2, start, "s10", SOURCES);
    expect(r.steps.map((s) => [s.id, s.do, s.what, s.count, s.assign, s.from, s.at_pop, s.withPrevious])).toEqual([
      ["s1", "build", "house", 2, "sheep", undefined, undefined, false],
      ["s2", "train", "villager", 3, "sheep", undefined, undefined, false],
      ["s10", "train", "villager", 1, "wood", undefined, undefined, false],
      ["s9", "build", "lumber_camp", 1, "wood", "wood", 8, true],
      ["s3", "train", "villager", 3, "wood", undefined, undefined, false],
    ]);
    expect(r.blockId).toBe("s10");
    expect(r.selectId).toBe("s9");
    expect(r.note).toBe("sale de los 4 que iban a madera");
    expect(isBuiltByNewVillager(r.steps, 3, start)).toBe(true);

    const labels = guideLabels(r.steps, start);
    expect(labels.get("s2")).toBe("Aldeanos 4 a 6 → ovejas");
    expect(labels.get("s9")).toBe("Aldeano 7: campamento maderero → madera");
    expect(labels.get("s3")).toBe("Aldeanos 8 a 10 → madera");
    expect(chainLabel([r.steps[2], r.steps[3]], r.steps, labels, () => "?")).toBe("Aldeano 7: campamento maderero → madera");
  });

  it("si lo que sigue va a otro lado, es un aldeano más; el campamento minero va al oro o a la piedra", () => {
    const toGold = [...BO.slice(0, 2), step("s3", { do: "train", what: "villager", count: 3, assign: "gold" })];
    const r = insertByNewVillager(toGold, camp, 2, start, "s10", SOURCES);
    expect(r.steps.map((s) => [s.id, s.count, s.assign])).toEqual([
      ["s1", 2, "sheep"],
      ["s2", 3, "sheep"],
      ["s10", 1, "wood"],
      ["s9", 1, "wood"],
      ["s3", 3, "gold"],
    ]);
    expect(r.note).toBeNull();
    const mining = insertByNewVillager(toGold, step("s9", { do: "build", what: "mining_camp" }), 2, start, "s10", SOURCES);
    expect(mining.steps[2].assign).toBe("gold");
    expect(mining.steps[4].count).toBe(2);
  });

  it("en la paleta: con aldeanos sin tarea los usa, sin ellos lo hace el próximo aldeano; las granjas no", () => {
    const custom = [step("s2", { do: "train", what: "villager", count: 3, assign: "sheep" })];
    expect(insertNewStep(custom, camp, 1, start, "s10", SOURCES).steps.map((s) => s.id)).toEqual(["s2", "s9"]);
    expect(insertNewStep(BO, camp, 2, start, "s10", SOURCES).steps.map((s) => s.id)).toEqual(["s1", "s2", "s10", "s9", "s3"]);
    const farm = step("s9", { do: "build", what: "farm", from: "sheep", assign: "farm" });
    expect(insertNewStep(BO, farm, 3, start, "s10", SOURCES).steps.map((s) => s.id)).toEqual(["s1", "s2", "s3", "s9"]);
  });

  it("aldeanos al mismo lugar que la tanda de arriba se suman a ella", () => {
    const wood = step("s9", { do: "train", what: "villager", count: 1, assign: "wood" });
    const r = insertNewStep(BO, wood, 3, start, "s10", SOURCES);
    expect(r.steps.map((s) => [s.id, s.count])).toEqual([
      ["s1", 2],
      ["s2", 3],
      ["s3", 5],
    ]);
    expect(r.selectId).toBe("s3");
    // A otro lugar, o pegado al aldeano de un edificio, es una tanda nueva.
    expect(insertNewStep(BO, { ...wood, assign: "gold" }, 3, start, "s10", SOURCES).steps).toHaveLength(4);
    const withCamp = insertByNewVillager(BO, camp, 2, start, "s10", SOURCES).steps;
    expect(insertNewStep(withCamp, wood, 3, start, "s11", SOURCES).steps.find((s) => s.id === "s10")!.count).toBe(1);
  });

  it("si cambia lo que hay antes, el edificio sigue esperando a su aldeano", () => {
    const r = insertByNewVillager(BO, camp, 2, start, "s10", SOURCES).steps;
    const ids = newVillagerBuilds(r, start);
    expect([...ids]).toEqual(["s9"]);
    // Un aldeano más a las bayas antes: ahora es el aldeano 8 y arranca a 9 de población.
    const more = [r[0], r[1], step("s11", { do: "train", what: "villager", count: 1, assign: "berries" }), ...r.slice(2)];
    const synced = syncNewVillagerBuilds(more, ids, start);
    expect(synced.find((s) => s.id === "s9")!.at_pop).toBe(9);
    expect(guideLabels(synced, start).get("s9")).toBe("Aldeano 8: campamento maderero → madera");
    expect(syncNewVillagerBuilds(synced, ids, start)).toBe(synced);
    // Con otra civ (más población al empezar) también.
    expect(syncNewVillagerBuilds(r, ids, { ...start, population: 7, idle_villagers: 6 }).find((s) => s.id === "s9")!.at_pop).toBe(11);
  });

  it("deja de hacerlo el próximo aldeano o se borra: el aldeano vuelve a su tanda", () => {
    const r = insertByNewVillager(BO, camp, 2, start, "s10", SOURCES).steps;
    const detached = detachNewVillager(r, "s9", "sheep");
    expect(detached.map((s) => [s.id, s.count, s.from, s.at_pop, s.withPrevious])).toEqual([
      ["s1", 2, undefined, undefined, false],
      ["s2", 3, undefined, undefined, false],
      ["s9", 1, "sheep", undefined, false],
      ["s3", 4, undefined, undefined, false],
    ]);
    expect(removeNewVillagerBuild(r, "s9")!.map((s) => [s.id, s.count])).toEqual([
      ["s1", 2],
      ["s2", 3],
      ["s3", 4],
    ]);
    expect(removeNewVillagerBuild(BO, "s3")).toBeNull();
  });
});

describe("el editor por aldeano con el Core", () => {
  let coach: CoachRuntime;
  beforeAll(async () => {
    coach = await startTestCoach();
  }, 60_000);

  const analyze = (steps: EditorStep[]): Analysis => {
    const result = coach.analyze(JSON.stringify(toJson({ bo: { id: "t", name: "t", civ: "any", map: "any", author: "", description: "", tags: [] }, steps })));
    if (!result.ok) throw new Error(result.error);
    return result;
  };

  it("el campamento arranca cuando sale el aldeano 7 (1:40), no apenas puede", () => {
    const before = analyze([...BO.slice(0, 2), { ...camp, from: "sheep", assign: "wood" }, BO[2]]);
    expect(before.steps.find((s) => s.id === "s9")!.start).toBeLessThan(60);

    const r = insertByNewVillager(BO, camp, 2, start, "s10", SOURCES).steps;
    const a = analyze(r);
    expect(a.valid).toBe(true);
    expect(a.issues.filter((i) => i.severity === "error")).toEqual([]);
    expect(a.steps.find((s) => s.id === "s9")!.start).toBe(100);
    expect(a.steps.find((s) => s.id === "s10")!.end).toBe(100);
  });
});
