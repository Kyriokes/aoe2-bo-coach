import { describe, expect, it, vi } from "vitest";
import { defaultPlan, newAssisted } from "./assisted";
import { clearDraft, comeBackOnClick, draftJson, draftKey, leaveAndComeBack, losesWork, markUnsaved, openingApp, readDraft, warnBeforeUnload, writeDraft } from "./draft";
import { initialState, isDirty, reduce, type EditorState } from "./model";

/** Un sessionStorage de mentira. */
function memoryStorage() {
  const items = new Map<string, string>();
  return {
    get length() {
      return items.size;
    },
    key: (index: number) => [...items.keys()][index] ?? null,
    getItem: (key: string) => items.get(key) ?? null,
    setItem: (key: string, value: string) => void items.set(key, value),
    removeItem: (key: string) => void items.delete(key),
  };
}

/** Un sessionStorage que el navegador no deja usar (modo privado, sin lugar). */
const brokenStorage = {
  get length(): number {
    throw new Error("SecurityError");
  },
  key: () => {
    throw new Error("SecurityError");
  },
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

// Next esconde /subir en <Activity hidden> al ir a otra página del sitio y React corre los cleanups: un aviso atado a la
// vista se iba, y cerrar la pestaña desde la biblioteca perdía la BO sin preguntar.
describe("antes de salir de la página", () => {
  const json = { json: "{}", visibility: "private" as const };

  it("cerrar la pestaña pierde lo que no se guardó, esté o no el editor a la vista: los borradores se van con ella", () => {
    const storage = memoryStorage();
    expect(losesWork(storage, [], false)).toBe(false);

    writeDraft(storage, draftKey("k7m2qa"), json);
    expect(losesWork(storage, [draftKey("k7m2qa")], false)).toBe(true);
    // El editor ya no está (otra ruta, o se desmontó), pero el borrador sí.
    expect(losesWork(storage, [], false)).toBe(true);
  });

  it("salir para volver a esta pestaña (el login) no pregunta si lo que no se guardó quedó en el borrador", () => {
    const storage = memoryStorage();
    writeDraft(storage, draftKey(), json);
    expect(losesWork(storage, [draftKey()], true)).toBe(false);
    expect(losesWork(storage, [], true)).toBe(false);
    // Si el navegador no dejó escribirlo, lo único que hay es lo de la página: pregunta igual.
    expect(losesWork(storage, [draftKey("otra")], true)).toBe(true);
    expect(losesWork(brokenStorage, [draftKey()], true)).toBe(true);
    expect(losesWork(null, [draftKey()], true)).toBe(true);
  });

  it("lo que no es un borrador del editor no cuenta, y sin almacenamiento solo cuenta lo que marcó el editor", () => {
    const storage = memoryStorage();
    storage.setItem("otra.cosa", "x");
    storage.setItem(draftKey("rota"), "{ roto");
    expect(losesWork(storage, [], false)).toBe(false);
    expect(losesWork(brokenStorage, [], false)).toBe(false);
    expect(losesWork(null, [], false)).toBe(false);
    expect(losesWork(null, [draftKey()], false)).toBe(true);
  });

  /** Lo que hace el navegador al cerrar la pestaña: si el aviso pide quedarse, pregunta. */
  const asks = () => {
    const event = { preventDefault: vi.fn(), returnValue: undefined as unknown };
    warnBeforeUnload(event as unknown as BeforeUnloadEvent);
    return event.preventDefault.mock.calls.length > 0 && event.returnValue === "";
  };

  it("el aviso mira lo que marcó el editor y los borradores, no si el editor está a la vista; salir para volver vale una vez", () => {
    const storage = memoryStorage();
    const key = draftKey("k7m2qa");
    vi.stubGlobal("window", { sessionStorage: storage });
    try {
      expect(asks()).toBe(false);
      markUnsaved(key, true);
      expect(asks()).toBe(true);
      writeDraft(storage, key, json);
      expect(asks()).toBe(true);
      leaveAndComeBack();
      expect(asks()).toBe(false);
      expect(asks()).toBe(true);
      markUnsaved(key, false);
      clearDraft(storage, key);
      expect(asks()).toBe(false);
    } finally {
      markUnsaved(key, false);
      vi.unstubAllGlobals();
    }
  });

  // Chrome dispara beforeunload con un enlace bocoach:// y con location.href a ese esquema, aunque la página no se va: con
  // Cancelar, "Abrir en la app" no abría la app y "Conectar la app" creaba un token que no le llegaba.
  it("abrir la app no pregunta aunque haya algo sin guardar, porque la página se queda; vale solo para esa salida", () => {
    const storage = memoryStorage();
    const key = draftKey();
    vi.stubGlobal("window", { sessionStorage: storage });
    vi.useFakeTimers();
    try {
      // Ni siquiera quedó en el borrador: lo único que hay está en la página, y la página no se descarga.
      markUnsaved(key, true);
      expect(asks()).toBe(true);
      openingApp();
      expect(asks()).toBe(false);
      expect(asks()).toBe(true);
      // Si el navegador no dispara el aviso (no abrió nada, o lo abrió en otra pestaña), no queda prendido.
      openingApp();
      vi.runAllTimers();
      expect(asks()).toBe(true);
    } finally {
      markUnsaved(key, false);
      vi.useRealTimers();
      vi.unstubAllGlobals();
    }
  });

  // /idioma guarda la elección y vuelve a esta pestaña, en la misma página: el borrador se recupera solo.
  it("cambiar de idioma no pregunta, desde el editor o desde otra página; abrir el enlace en otra pestaña no sale de esta", () => {
    const storage = memoryStorage();
    const key = draftKey();
    const click = { button: 0, ctrlKey: false, metaKey: false, shiftKey: false, altKey: false };
    vi.stubGlobal("window", { sessionStorage: storage });
    try {
      writeDraft(storage, key, json);
      expect(asks()).toBe(true);
      comeBackOnClick(click);
      expect(asks()).toBe(false);

      markUnsaved(key, true);
      comeBackOnClick(click);
      expect(asks()).toBe(false);

      // Con Ctrl, Mayús, Cmd o Alt el navegador lo abre en otra pestaña o ventana (o lo descarga): esta se queda, y cerrarla
      // después tiene que preguntar.
      for (const modifier of ["ctrlKey", "metaKey", "shiftKey", "altKey"] as const) {
        comeBackOnClick({ ...click, [modifier]: true });
        expect(asks()).toBe(true);
      }
      comeBackOnClick({ ...click, button: 1 });
      expect(asks()).toBe(true);
    } finally {
      markUnsaved(key, false);
      vi.unstubAllGlobals();
    }
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
