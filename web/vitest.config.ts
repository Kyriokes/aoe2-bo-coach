import { fileURLToPath } from "node:url";
import { defineConfig } from "vitest/config";

export default defineConfig({
  resolve: {
    // Los módulos de servidor (import "server-only") se pueden probar con sus dependencias reemplazadas por vi.mock.
    alias: { "server-only": fileURLToPath(new URL("./vitest.server-only.ts", import.meta.url)) },
  },
  test: {
    environment: "node",
    include: ["supabase/tests/**/*.test.ts", "src/**/*.test.ts"],
    testTimeout: 30_000,
    // Levantar PGlite en el beforeEach de los tests de la base pasa los 10 s por defecto con la PC cargada.
    hookTimeout: 60_000,
  },
});
