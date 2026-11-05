import { describe, expect, it } from "vitest";
import { AUTHOR_MESSAGE, dbErrorMessage, dbErrorStatus, LIMITS_MESSAGE } from "./dbErrors";

describe("errores de la base", () => {
  it("una restricción violada se explica con los topes", () => {
    const error = { code: "23514", message: 'new row for relation "build_orders" violates check constraint "build_orders_body_ok"' };
    expect(dbErrorMessage(error)).toBe(LIMITS_MESSAGE);
    expect(dbErrorStatus(error)).toBe(400);
    const author = { code: "23514", message: 'new row for relation "build_orders" violates check constraint "build_orders_author_ok"' };
    expect(dbErrorMessage(author)).toBe(AUTHOR_MESSAGE);
    expect(dbErrorStatus(author)).toBe(400);
  });

  it("los mensajes propios de la migración se muestran tal cual", () => {
    const tooMany = { code: "23514", message: "Publicaste muchas build orders en las últimas 24 horas: probá de nuevo mañana." };
    expect(dbErrorMessage(tooMany)).toBe(tooMany.message);
    const token = { code: "28000", message: "El token de la app no es válido: creá uno nuevo en la web." };
    expect(dbErrorMessage(token)).toBe(token.message);
    expect(dbErrorStatus(token)).toBe(401);
  });

  it("lo inesperado no muestra detalles internos", () => {
    const error = { code: "42P01", message: 'relation "public.build_orders" does not exist' };
    expect(dbErrorMessage(error)).not.toContain("relation");
    expect(dbErrorStatus(error)).toBe(500);
    expect(dbErrorMessage({ code: "42501", message: "new row violates row-level security policy for table \"votes\"" })).toBe(
      "No tenés permiso para hacer eso.",
    );
  });
});

describe("datos que la base no puede guardar (clase 22)", () => {
  it.each(["22003", "22P02", "22P05", "22021"])("%s da 400, explicado con los topes y sin el detalle", (code) => {
    const error = { code, message: 'value "10000000000" is out of range for type integer' };
    expect(dbErrorStatus(error)).toBe(400);
    expect(dbErrorMessage(error)).toBe(LIMITS_MESSAGE);
    expect(dbErrorMessage(error, "en")).toMatch(/limits/);
  });
});

describe("cambios muy seguidos", () => {
  const error = { code: "BC430", message: "Guardaste esta build order hace unos segundos: esperá un momento y probá de nuevo." };

  it("dan 429 y el mensaje de la base, explicado en cada idioma", () => {
    expect(dbErrorStatus(error)).toBe(429);
    expect(dbErrorMessage(error)).toBe(error.message);
    expect(dbErrorMessage(error, "en")).toMatch(/seconds/);
  });
});

describe("BO que no existe o no es pública", () => {
  it("se muestra el mensaje de la base y da 404", () => {
    const error = { code: "P0002", message: "No hay ninguna build order pública con el código abcdef." };
    expect(dbErrorMessage(error)).toBe(error.message);
    expect(dbErrorStatus(error)).toBe(404);
  });
});
