# Modo asistido (e): Analyze y la web Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** que la web genere y muestre las BO asistidas: `Analyze` arma los pasos desde `assisted` y la pantalla A de la spec (pestaña Asistido en `/subir`) edita los objetivos y muestra el resultado.

**Architecture:** el Core genera dentro de `BuildOrderAnalysis.Analyze` (un solo punto de entrada para la web, sin exports nuevos del Wasm) y suma la sección `assisted` a la respuesta. La web tiene un modelo puro nuevo (`web/src/lib/editor/assisted.ts`: objetivos, franja "Último cambio", pasar a manual) y un componente `AssistedEditor` que se monta en `BuildOrderEditor` con pestañas Asistido / Manual.

**Tech Stack:** C# .NET 10 (Core, Wasm), Next.js 16 + React 19 + TypeScript, vitest.

**Spec:** `docs/superpowers/specs/2026-10-26-modo-asistido-design.md` (secciones 2, 5, 6, 8, 9 y 10 (e)).

## Global Constraints

- Textos en castellano rioplatense, sin rayas largas; un commit por cambio con `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- La web llama solo a `Analyze` (spec 5): no se agregan funciones exportadas al Wasm.
- La respuesta suma `assisted`: los porqués por paso, la hora de cada objetivo, los problemas y la versión del generador (spec 5).
- Meta de tiempo: menos de 300 ms en la web para "120 aldeanos hasta Imperial con 5 objetivos"; si no llega, el Core pasa a un Web Worker (spec 5).
- Los toques seguidos de − y + se juntan: se espera a que dejen de llegar 150 ms antes de simular (spec 6).
- La BO generada valida, se publica y se descarga como cualquier otra (la normalizada que devuelve `Analyze`).
- Celular debajo de 640 px: una sola columna (`useNarrow`).

## Review Focus

1. **Una BO asistida con `assisted` mal armado** (un JSON tocado a mano): se muestra el error como en el editor manual y no se genera (spec 8). Test en la Tarea 1.
2. **Cambiar la civ a una que no tiene un objetivo** (francos a incas con exploradores): el objetivo queda en rojo con el motivo, no se borra. Test en la Tarea 2.
3. **"Pasar a manual" y volver a cargar**: la BO queda sin `assisted` y con los mismos pasos, editable en el editor de hoy. Test en la Tarea 2.
4. **Abrir una BO asistida guardada con pasos viejos** (el generador cambió): se regenera y avisa "Se recalculó con el generador nuevo". Test en la Tarea 1 (marca) y Tarea 3 (aviso).
5. **Muchos toques seguidos de + y −**: una sola simulación al final. Test en la Tarea 2 (el modelo del retraso).

---

### Task 1: Analyze genera las BO asistidas

**Files:** `src/BoCoach.Core/BuildOrders/BuildOrderAnalysis.cs`, `BuildOrderValidator.cs`; tests en `tests/BoCoach.Core.Tests/BuildOrders/` (el archivo de tests de `BuildOrderAnalysis`).

**Interfaces:**
- `BuildOrderValidator.ValidateAssistedPlan(BuildOrder bo): ValidationResult` (público: solo las reglas de `assisted`).
- `BuildOrderAnalysis.Analyze` (misma firma): si la BO trae `assisted`, primero `ValidateAssistedPlan`; con errores, responde `ok` true, `valid` false con esos problemas y sin generar; sin errores, genera con `AssistedGenerator` (con `maps` o `Maps` vacío) y sigue con la BO generada, simulada con `AssistedGenerator.Settings`. La respuesta suma `assisted`: `{ version, regenerated, reasons: { <id>: <porqué> }, goals: [{ index, start, end, problem }], problems: [...] }`; `regenerated` es true si la BO traía pasos y los generados son otros.

- [ ] Tests: `Una_BO_asistida_se_genera_y_se_analiza` (francos, `until` 60, Feudal: `valid` true, pasos generados con su porqué, `assisted.goals[0].end` con número, `build_order` trae los pasos y `assisted`); `Con_assisted_mal_armado_no_se_genera` (`until` 0: `valid` false con el error de `assisted.villagers.until` y sin pasos); `Con_pasos_viejos_avisa_que_se_regenero` (la misma BO con un paso cualquiera guardado: `regenerated` true; con los pasos que generó: false).
- [ ] Correr y ver fallar; implementar; correr `dotnet test tests/BoCoach.Core.Tests` en verde; commit.

### Task 2: el modelo de la web

**Files:** `web/src/lib/coach/types.ts` (sección `assisted`), `web/src/lib/editor/model.ts` (`EditorBo.assisted`, `toJson` la manda, `fromAnalysis` la toma), crear `web/src/lib/editor/assisted.ts` con sus tests, y un test con el Core en Node (como `coach.test.ts`).

**Interfaces de `assisted.ts`:** tipos `AssistedPlan`, `AssistedGoal` (mismos nombres que el JSON); `defaultPlan()` (aldeanos hasta 120, Feudal, Castillos; apertura y `eco_upgrades` como el Core por defecto); `addGoal`, `removeGoal`, `moveGoal(plan, from, to)`, `updateGoal(plan, index, patch)`; `goalLabel(goal, names)`; `diffGoals(before: Analysis|null, after: Analysis, change: string): ChangeStrip | null` con `{ change, shifts: [{ label, before, after, delta }] }`; `toManual(state)`: la BO sin `assisted` y con los pasos del análisis en unidades; `debounce(fn, ms)` para juntar los toques (150 ms).

- [ ] Tests vitest: agregar, quitar, mover y cambiar objetivos; la franja ("establos 2 a 3" y "Castillos 18:20 a 19:00 (+0:40)"); `toManual` (sin `assisted`, mismos pasos); el retraso junta 5 llamadas en una; con el Core en Node: una BO asistida por `Analyze` vuelve con pasos y `assisted`, francos a incas con exploradores deja el objetivo con el motivo, y "120 aldeanos hasta Imperial con 5 objetivos" se mide (si pasa de 300 ms, ver la Tarea 3).
- [ ] Correr y ver fallar; implementar; `cd web && npm test` en verde; commit.

### Task 3: la pantalla Asistido

**Files:** crear `web/src/components/editor/AssistedEditor.tsx` (y lo que haga falta para la lista de objetivos y la franja); modificar `BuildOrderEditor.tsx` (pestañas Asistido / Manual; una BO nueva arranca en Asistido con `defaultPlan`, una cargada sin `assisted` en Manual); `EditorTimeline` acepta un modo solo para mirar con líneas verticales en la hora de cada objetivo cumplido.

Pantalla A (spec 6): izquierda, "De fondo" (apertura en una línea con "Cambiar", aldeanos hasta con deslizador y número, mejoras eco, casas siempre en gris) y "Objetivos, en orden de prioridad" (número, nombre, sus controles, hora y diferencia en verde o rojo, el motivo en rojo, ↑ y ↓ con el teclado, borrar), "+ Agregar objetivo" (edad, unidades, centros urbanos o tecnología, solo lo que la civ puede, con los nombres de la paleta) y "Pasar a manual" con confirmación. Derecha: la franja "Último cambio", la línea de tiempo para mirar y los pasos con `guideLabels` y su porqué en gris. Debajo de 640 px, una columna. Si `assisted.regenerated`, avisa "Se recalculó con el generador nuevo". Si el análisis tarda más de 300 ms, el Core corre en un Web Worker.

- [ ] Verificar en el navegador integrado (`preview_start`, con el panel a la vista): agregar 20 exploradores con 2 establos, pasar a 3 y ver la franja; captura de PC y de celular; consola sin errores; `npm run lint` y `npm test` en verde; commit.

### Task 4: docs

- [ ] `PLAN.md` D12: "(e) hecho" con lo que se midió (tiempo de `Analyze` asistido en Node) y lo que queda para (f); `CLAUDE.md`: el renglón de D12; `docs/pendientes-usuario.md`: probar la pestaña Asistido en la vista previa de Vercel. Commit y push.
