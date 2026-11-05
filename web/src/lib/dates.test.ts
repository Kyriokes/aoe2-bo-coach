import { describe, expect, it } from "vitest";
import { dayOf } from "./dates";

describe("la fecha de un token", () => {
  // El servidor corre en UTC y el navegador en la zona del usuario: con la zona de cada uno, cerca de la medianoche la fecha
  // del HTML no coincidía con la del navegador y React rearmaba la página (un error de hidratación).
  it("es la misma en cualquier zona horaria", () => {
    const zone = process.env.TZ;
    try {
      for (const tz of ["UTC", "America/Argentina/Buenos_Aires", "Asia/Tokyo"]) {
        process.env.TZ = tz;
        expect(dayOf("2026-11-05T01:30:00Z", "es")).toBe("5/11/2026");
        expect(dayOf("2026-11-05T01:30:00Z", "en")).toBe("11/5/2026");
      }
    } finally {
      if (zone === undefined) delete process.env.TZ;
      else process.env.TZ = zone;
    }
  });
});
