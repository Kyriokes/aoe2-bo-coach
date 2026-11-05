# CÃ³mo se fueron cerrando las fases

Estado: histÃ³rico. Las fases 0 a 10 de `PLAN.md` estÃ¡n hechas: lo que dice "en curso" acÃ¡ se refiere a ese momento, y las ramas `fase-N` dejaron de usarse (todo se juntÃ³ en `fase-9-refinamiento`). Movido tal cual del `CLAUDE.md` viejo el 2026-11-05.

- **Editor de BO dentro de la app** (Fase 4) con vista previa simulada. **Web con biblioteca para compartir** en la última fase (Fase 10). App y web usan el mismo formato JSON con `schema_version`.
- Fase 0 (setup) completa: CI en verde en GitHub.
- Fase 1 (datos del juego) completa: extractor, v8.9.json y tests de valores clave.
- Fase 2 (modelo de BO) completa: serializador, validador, events.json y 3 BO de ejemplo.
- Fase 3 (motor) completa: PlayerState, Tracker, Planner y LivePlan con tests de los 5 escenarios.
- Fase 5 (economía v1) completa: RateObserver, EconomyAdvisor (estimación de aldeanos, demanda, déficit, greedy) y SuggestionGate.
- Fase 4 (editor WPF) hecha y verificada por código (tests de ViewModels y capturas con --screenshot). La prueba de usabilidad del usuario (BO de 20 pasos en menos de 5 minutos) quedó pendiente (`PLAN.md`, sección 11, B5).
- Fase 8 (overlay e integración) en curso, rama `fase-8-overlay`:
- Fase 10 (web) en curso, rama `fase-10-web` (el usuario la pidió con la Fase 8 esperando su partida en vivo):
- Fase 9 (refinamiento) en curso, rama `fase-9-refinamiento` (el usuario está fuera de casa: se avanza con lo que no lo necesita en la PC):
- Las ideas grandes (la BO como postura de cierre con prioridades, ramas disparadas por señales, elegir la BO por civ, rival y mapa, informe de después de la partida) están en `PLAN.md`, sección 11, bloque D, y se diseñan con el usuario antes de codearlas. Postura de "Scouts Feudal (22 pop)" según el usuario: siempre 1 aldeano y 1 scout en cola y clic en Castillos (pide dos edificios de Feudal y 200 de oro, que la BO todavía no tiene).
- Lo que solo puede hacer el usuario (grabar, probar en su PC, pedir el push a main, publicar) se anota en `docs/pendientes-usuario.md`.
