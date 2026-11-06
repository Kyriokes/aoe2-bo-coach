import { describe, expect, it } from "vitest";
import { defaultPlan, newAssisted } from "./assisted";
import { clearDraft, draftJson, draftKey, readDraft, writeDraft } from "./draft";
import { initialState, isDirty, reduce, type EditorState } from "./model";

/** Un sessionStorage de mentira. */
function memoryStorage() {
  const items = new Map<string, string>();
  return {
    getItem: (key: string) => items.get(key) ?? null,
    setItem: (key: string, value: string) => void items.set(key, value),
    removeItem: (key: string) => void items.delete(key),
  };
}

/** Un sessionStorage que el navegador no deja usar (modo privado, sin lugar). */
const brokenStorage = {
  getItem: () => {
    throw new Error("SecurityError");
  },
  setItem: () => {
    throw new Error("QuotaExceededError");
  },
  removeItem: () => {
    throw new Error("SecurityError");
  },
};

describe("el borrador del editor", () => {
  it("se guarda y se lee por BO: la nueva y cada una que se edita van aparte", () => {
    const storage = memoryStorage();
    writeDraft(storage, draftKey(), { json: "{nueva}", visibility: "private" });
    writeDraft(storage, draftKey("k7m2qa"), { json: "{editada}", visibility: "public" });

    expect(readDraft(storage, draftKey())).toEqual({ json: "{nueva}", visibility: "private" });
    expect(readDraft(storage, draftKey("k7m2qa"))).toEqual({ json: "{editada}", visibility: "public" });
    expect(readDraft(storage, draftKey("otra"))).toBeNull();

    clearDraft(storage, draftKey());
    expect(readDraft(storage, draftKey())).toBeNull();
    expect(readDraft(storage, draftKey("k7m2qa"))).not.toBeNull();
  });

  it("si el navegador no deja usar el almacenamiento, el editor sigue sin borrador", () => {
    expect(() => writeDraft(brokenStorage, draftKey(), { json: "{}", visibility: "private" })).not.toThrow();
    expect(readDraft(brokenStorage, draftKey())).toBeNull();
    expect(() => clearDraft(brokenStorage, draftKey())).not.toThrow();
    expect(readDraft(null, draftKey())).toBeNull();
    expect(() => writeDraft(null, draftKey(), { json: "{}", visibility: "private" })).not.toThrow();
    expect(() => clearDraft(null, draftKey())).not.toThrow();
  });

  it("lo que no se entiende no es un borrador", () => {
    const storage = memoryStorage();
    storage.setItem(draftKey(), "{ roto");
    expect(readDraft(storage, draftKey())).toBeNull();
    storage.setItem(draftKey(), JSON.stringify({ version: 999, json: "{}", visibility: "public" }));
    expect(readDraft(storage, draftKey())).toBeNull();
    storage.setItem(draftKey(), JSON.stringify({ version: 1, json: 7 }));
    expect(readDraft(storage, draftKey())).toBeNull();
    // Una visibilidad rara queda privada, como en el servidor.
    storage.setItem(draftKey(), JSON.stringify({ version: 1, json: "{}", visibility: "x" }));
    expect(readDraft(storage, draftKey())?.visibility).toBe("private");
  });

  it("una BO manual guarda sus pasos; una asistida, sus objetivos (los pasos se generan al abrirla)", () => {
    const manual = reduce(reduce(initialState(), { type: "new", id: "n1" }), { type: "setField", field: "name", value: "Mía" });
    expect(JSON.parse(draftJson(manual))).toMatchObject({ name: "Mía", steps: [{ id: "s1", do: "build", what: "house" }] });

    const assisted: EditorState = { ...manual, bo: { ...manual.bo, assisted: defaultPlan() } };
    const json = JSON.parse(draftJson(assisted));
    expect(json.steps).toEqual([]);
    expect(json.assisted.goals).toEqual(defaultPlan().goals);
  });
});

describe("si hay cambios sin guardar", () => {
  const empty = initialState();
  const fresh = newAssisted(empty, "n1");

  it("al abrir, con una BO nueva o con la que manda la página, no hay", () => {
    expect(isDirty(empty)).toBe(false);
    expect(isDirty(fresh)).toBe(false);
    expect(isDirty(reduce(empty, { type: "new", id: "n1" }))).toBe(false);
    const edited = reduce(fresh, { type: "setField", field: "name", value: "Mía" });
    expect(isDirty(reduce(edited, { type: "load", analysis: analysis(), from: "page" }))).toBe(false);
  });

  // Antes quedaba "sin cambios": sin borrador ni aviso, e iniciar sesión para publicarla volvía a "Nueva build order".
  it("lo que carga el usuario (un ejemplo, un archivo, una guía pegada) no está guardado en la web: los hay hasta guardar", () => {
    const loaded = reduce(fresh, { type: "load", analysis: analysis(), from: "user" });
    expect(isDirty(loaded)).toBe(true);
    expect(isDirty(reduce(loaded, { type: "saved" }))).toBe(false);
  });

  it("cualquier edición los hace, elegir una caja o que llegue un análisis no", () => {
    expect(isDirty(reduce(fresh, { type: "setField", field: "name", value: "Mía" }))).toBe(true);
    expect(isDirty(reduce(fresh, { type: "setTags", tags: ["arabia"] }))).toBe(true);
    expect(isDirty(reduce(fresh, { type: "setSteps", steps: [] }))).toBe(true);
    expect(isDirty(reduce(fresh, { type: "select", id: "s1" }))).toBe(false);
    expect(isDirty(reduce(fresh, { type: "analyzed", analysis: analysis(), revision: fresh.revision }))).toBe(false);
    // Lo que genera el modo asistido no es una edición del usuario.
    expect(isDirty(reduce(fresh, { type: "generated", analysis: analysis(), revision: fresh.revision }))).toBe(false);
  });

  it("un borrador recuperado sigue con cambios, y guardar los limpia", () => {
    const restored = reduce(fresh, { type: "load", analysis: analysis(), from: "draft" });
    expect(isDirty(restored)).toBe(true);
    expect(isDirty(reduce(restored, { type: "saved" }))).toBe(false);
  });
});

/** Un análisis mínimo. */
function analysis() {
  return {
    ok: true as const,
    valid: true,
    style: null,
    id: "bo1",
    name: "Prueba",
    civ: "any",
    map: null,
    author: null,
    description: null,
    schema_version: 2,
    tags: [],
    targets: {},
    start: { food: 200, wood: 200, gold: 100, stone: 200, population: 4, population_cap: 5, idle_villagers: 3, villagers: {}, houses: true },
    build_order: { steps: [] },
    issues: [],
    steps: [],
  };
}
