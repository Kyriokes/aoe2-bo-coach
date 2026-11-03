import { gzipSync } from "node:zlib";
import { describe, expect, it } from "vitest";
import { isAppVersion, isGameId, MAX_RECORD_BYTES, recordIdentity, recordProblem } from "./gameRecord";

const game = "c".repeat(32);
const zipped = (text: string) => gzipSync(Buffer.from(text));
const meta = (over: object = {}) => JSON.stringify({ t: "meta", schema: 1, game_id: game, ...over });

describe("registro de partida", () => {
  it("acepta un registro con su meta", () => {
    expect(recordProblem(zipped(`${meta()}\n{"t":"cap"}\n`), game)).toBeNull();
  });

  it("rechaza lo que no es un registro", () => {
    expect(recordProblem(new Uint8Array(), game)).toMatch(/1 MB/);
    expect(recordProblem(new Uint8Array(MAX_RECORD_BYTES + 1), game)).toMatch(/1 MB/);
    expect(recordProblem(Buffer.from("hola"), game)).toMatch(/gzip/);
    expect(recordProblem(zipped("no es json\n"), game)).toMatch(/meta/);
    expect(recordProblem(zipped('{"t":"cap"}\n'), game)).toMatch(/meta/);
    expect(recordProblem(zipped("null\n"), game)).toMatch(/meta/);
    expect(recordProblem(zipped(`${meta({ schema: "uno" })}\n`), game)).toMatch(/versión/);
    expect(recordProblem(zipped(`${meta({ game_id: "d".repeat(32) })}\n`), game)).toMatch(/otra partida/);
  });

  it("frena una bomba de gzip", () => {
    const bomb = gzipSync(Buffer.alloc(70 * 1024 * 1024));
    expect(bomb.length).toBeLessThan(MAX_RECORD_BYTES);
    expect(recordProblem(bomb, game)).toMatch(/grande/);
  });

  it("lee la identidad de las cabeceras", () => {
    const install = "a".repeat(32);
    const secret = "b".repeat(64);
    expect(recordIdentity(new Headers({ "X-BoCoach-Install": install, "X-BoCoach-Secret": secret }))).toEqual({ install, secret });
    expect(recordIdentity(new Headers({ "X-BoCoach-Install": install }))).toBeNull();
    expect(recordIdentity(new Headers({ "X-BoCoach-Install": "x", "X-BoCoach-Secret": secret }))).toBeNull();
  });

  it("valida el identificador y la versión", () => {
    expect(isGameId(game)).toBe(true);
    expect(isGameId("../otra")).toBe(false);
    expect(isGameId(null)).toBe(false);
    expect(isAppVersion("v0.9.85 | abc1234 | 02/11 22:00")).toBe(true);
    expect(isAppVersion("")).toBe(false);
    expect(isAppVersion("v".repeat(41))).toBe(false);
  });
});
