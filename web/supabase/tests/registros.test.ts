import { beforeEach, describe, expect, it } from "vitest";
import { as, database, type Db } from "./database";

const install = "a".repeat(32);
const secret = "b".repeat(64);
const game = "c".repeat(32);
const other = "d".repeat(32);

type Reserved = { app_record_reserve: { path: string; status: string } };

async function reserve(db: Db, args: { install?: string; secret?: string; game?: string; size?: number; version?: string } = {}) {
  const { rows } = await as<Reserved>(db, null, "select public.app_record_reserve($1, $2, $3, $4, $5)", [
    args.install ?? install,
    args.secret ?? secret,
    args.game ?? game,
    args.size ?? 50_000,
    args.version ?? "v0.9.85",
  ]);
  return rows[0].app_record_reserve;
}

const gameId = (i: number) => i.toString(16).padStart(32, "0");

/** Sube un archivo como lo hace Storage, que anota su tamaño en metadata. */
const upload = (db: Db, name: string, size = 50_000) =>
  as(db, null, "insert into storage.objects (bucket_id, name, metadata) values ('registros', $1, $2)", [name, { size }]);

const markDone = (db: Db, args: { install?: string; secret?: string; game?: string } = {}) =>
  as(db, null, "select public.app_record_done($1, $2, $3)", [args.install ?? install, args.secret ?? secret, args.game ?? game]);

describe("registros de partida", () => {
  let db: Db;

  beforeEach(async () => {
    db = await database({ storage: true });
  });

  it("la primera reserva crea la instalación y da una ruta por partida", async () => {
    expect(await reserve(db)).toEqual({ path: `${install}/${game}.jsonl.gz`, status: "new" });
    const { rows } = await db.query<{ secret_hash: string }>("select secret_hash from public.record_installs");
    expect(rows).toHaveLength(1);
    expect(rows[0].secret_hash).not.toBe(secret); // se guarda el hash
  });

  it("otro secreto para la misma instalación no pasa", async () => {
    await reserve(db);
    await expect(reserve(db, { secret: "e".repeat(64), game: other })).rejects.toMatchObject({ code: "28000" });
    await expect(as(db, null, "select public.app_record_done($1, $2, $3)", [install, "e".repeat(64), game])).rejects.toMatchObject({ code: "28000" });
    await expect(as(db, null, "select public.app_record_forget($1, $2)", [install, "e".repeat(64)])).rejects.toMatchObject({ code: "28000" });
  });

  it("sin haber reservado nunca, marcar subido o pedir el borrado no crea la instalación", async () => {
    const forget = await as<{ app_record_forget: string[] }>(db, null, "select public.app_record_forget($1, $2)", [install, secret]);
    expect(forget.rows[0].app_record_forget).toEqual([]);
    await expect(as(db, null, "select public.app_record_done($1, $2, $3)", [install, secret, game])).rejects.toMatchObject({ code: "28000" });
    await expect(as(db, null, "select public.app_record_forget($1, $2)", ["x", secret])).rejects.toMatchObject({ code: "23514" });

    expect((await db.query("select 1 from public.record_installs")).rows).toEqual([]);
    await expect(as(db, null, "select public.record_install_known($1, $2)", [install, secret])).rejects.toThrow(/permission/);
  });

  it("reservar otra vez sin haber subido vuelve a dar la misma ruta, y ya subida dice que existe", async () => {
    const first = await reserve(db);
    expect(await reserve(db)).toEqual(first);

    await upload(db, first.path);
    await markDone(db);

    expect(await reserve(db)).toEqual({ path: first.path, status: "exists" });
    const { rows } = await db.query("select 1 from public.game_records");
    expect(rows).toHaveLength(1);
  });

  it("valida los formatos, el tamaño y la versión", async () => {
    await expect(reserve(db, { install: "x" })).rejects.toMatchObject({ code: "23514" });
    await expect(reserve(db, { secret: "corto" })).rejects.toMatchObject({ code: "23514" });
    await expect(reserve(db, { game: "../../otra" })).rejects.toMatchObject({ code: "23514" });
    await expect(reserve(db, { size: 1048577 })).rejects.toMatchObject({ code: "23514" });
    await expect(reserve(db, { size: 0 })).rejects.toMatchObject({ code: "23514" });
    await expect(reserve(db, { version: "v".repeat(41) })).rejects.toMatchObject({ code: "23514" });
  });

  it("hasta 30 partidas por instalación y por día; la reservada de ayer no cuenta", async () => {
    for (let i = 0; i < 30; i++) await reserve(db, { game: gameId(i) });
    await expect(reserve(db, { game: gameId(30) })).rejects.toMatchObject({ code: "BC429" });
    // Otra instalación sí puede.
    expect((await reserve(db, { install: "f".repeat(32), game: gameId(30) })).status).toBe("new");

    await db.exec("update public.game_records set reserved_at = now() - interval '2 days'");
    expect((await reserve(db, { game: gameId(30) })).status).toBe("new");
  });

  describe("topes y vencimiento de las reservas", () => {
    const stranger = "9".repeat(32);

    /** Una instalación que existe hace días, con el secreto de siempre. */
    const oldInstall = (id: string) =>
      db.query("insert into public.record_installs (id, secret_hash, created_at) values ($1, public.hash_token($2), now() - interval '2 days')", [id, secret]);

    /** Una instalación recién creada. */
    const newInstall = (id: string) => db.query("insert into public.record_installs (id, secret_hash) values ($1, 'x')", [id]);

    /** n reservas sin archivo de esa instalación, hechas hace ese tiempo. */
    const reservations = (id: string, n: number, ago: string, size = 1_048_576) =>
      db.query(
        `insert into public.game_records (install_id, game_id, path, size, app_version, reserved_at)
         select $1, lpad(to_hex(n), 32, '0'), $1 || '/' || n, $2, 'v', now() - $3::interval from generate_series(1, $4::int) n`,
        [id, size, ago, n],
      );

    it("900 reservas viejas sin subir no agotan el lugar", async () => {
      await oldInstall(stranger);
      await reservations(stranger, 900, "2 days");
      expect((await reserve(db)).status).toBe("new");
    });

    it("una reserva de las últimas 24 horas ocupa lo más que puede pesar, aunque diga 1 byte", async () => {
      await oldInstall(stranger);
      await reservations(stranger, 900, "1 hour", 1);
      await expect(reserve(db)).rejects.toMatchObject({ code: "BC429" });
      await db.exec("update public.game_records set reserved_at = now() - interval '25 hours'");
      expect((await reserve(db)).status).toBe("new");
    });

    it("una reserva vencida no deja subir, y reservar de nuevo pasa por los topes", async () => {
      const { path } = await reserve(db);
      await db.exec("update public.game_records set reserved_at = now() - interval '2 days'");
      await expect(upload(db, path)).rejects.toThrow(/row-level security/);

      for (let i = 0; i < 30; i++) await reserve(db, { game: gameId(i) });
      await expect(reserve(db)).rejects.toMatchObject({ code: "BC429" });

      await db.exec("update public.game_records set reserved_at = now() - interval '2 days'");
      expect(await reserve(db)).toEqual({ path, status: "new" });
      await upload(db, path);
      await markDone(db);
      expect(await reserve(db)).toEqual({ path, status: "exists" });
    });

    it("un archivo subido sin avisar sigue contando aunque su reserva venza", async () => {
      await oldInstall(stranger);
      const { path } = await reserve(db, { size: 1 });
      await upload(db, path, 1_048_576);
      await db.exec(`update public.game_records set reserved_at = now() - interval '2 days';
        insert into storage.objects (bucket_id, name, metadata)
        select 'registros', 'x/' || n, jsonb_build_object('size', 1048576) from generate_series(1, 899) n`);
      await expect(reserve(db, { install: stranger, game: other })).rejects.toMatchObject({ code: "BC429" });
    });

    it("las instalaciones de menos de un día tienen su cupo y no frenan a las viejas", async () => {
      await newInstall(stranger);
      await reservations(stranger, 300, "1 hour", 1);
      await expect(reserve(db)).rejects.toMatchObject({ code: "BC429" });
      await oldInstall(other);
      expect((await reserve(db, { install: other })).status).toBe("new");
    });

    it("las viejas tampoco frenan a las nuevas", async () => {
      await oldInstall(stranger);
      await reservations(stranger, 1000, "1 hour");
      await db.exec("update public.game_records set uploaded_at = now()"); // subidas: lo que pesan está en Storage, acá nada
      await oldInstall(other);
      await expect(reserve(db, { install: other })).rejects.toMatchObject({ code: "BC429" });
      expect((await reserve(db)).status).toBe("new");
    });
  });

  it("marcar subido exige el archivo y anota su tamaño real", async () => {
    const { path } = await reserve(db, { size: 1 });
    const row = async () =>
      (await db.query<{ size: number; uploaded: boolean }>("select size, uploaded_at is not null as uploaded from public.game_records")).rows[0];

    await expect(markDone(db)).rejects.toThrow(/Storage/);
    expect(await row()).toEqual({ size: 1, uploaded: false });

    await upload(db, path, 1_048_576);
    await markDone(db);
    expect(await row()).toEqual({ size: 1_048_576, uploaded: true });
  });

  it("el tope mira lo que hay en Storage, no lo que dijo la reserva", async () => {
    // Reservas que dijeron 1 byte, subidas de 1 MB directo a Storage y sin avisar que terminaron.
    await db.exec(`insert into public.record_installs (id, secret_hash) values ('${"9".repeat(32)}', 'x');
      insert into public.game_records (install_id, game_id, path, size, app_version, reserved_at)
      select '${"9".repeat(32)}', lpad(to_hex(n), 32, '0'), 'x/' || n, 1, 'v', now() - interval '2 days' from generate_series(1, 900) n;
      insert into storage.objects (bucket_id, name, metadata)
      select 'registros', 'x/' || n, jsonb_build_object('size', 1048576) from generate_series(1, 900) n`);
    await expect(reserve(db)).rejects.toMatchObject({ code: "BC429" });

    // Lo que se borra de Storage deja de contar.
    await db.exec("delete from storage.objects where name in ('x/1', 'x/2')");
    expect((await reserve(db)).status).toBe("new");
  });

  it("con la clave pública no se lee nada de las tablas", async () => {
    await reserve(db);
    await expect(as(db, null, "select * from public.record_installs")).rejects.toThrow(/permission/);
    await expect(as(db, null, "select * from public.game_records")).rejects.toThrow(/permission/);
    await expect(as(db, null, "insert into public.game_records (install_id, game_id, path, size, app_version) values ('a', 'b', 'c', 1, 'v')")).rejects.toThrow(/permission/);
    await expect(as(db, null, "select public.record_install($1, $2)", [install, secret])).rejects.toThrow(/permission/);
  });

  it("Storage: solo se sube a una ruta reservada y una sola vez", async () => {
    const { path } = await reserve(db);

    await expect(upload(db, `${install}/otra.jsonl.gz`)).rejects.toThrow(/row-level security/);
    await upload(db, path);

    await markDone(db);
    await expect(upload(db, path)).rejects.toThrow(/row-level security/);
    // Y los archivos no se ven ni se borran.
    expect((await as(db, null, "select * from storage.objects")).rows).toEqual([]);
    expect((await as(db, null, "delete from storage.objects returning name")).rows).toEqual([]);
  });

  it("el bucket es privado y con tope", async () => {
    const { rows } = await db.query("select public, file_size_limit, allowed_mime_types from storage.buckets where id = 'registros'");
    expect(rows).toEqual([{ public: false, file_size_limit: 1048576, allowed_mime_types: ["application/gzip"] }]);
  });

  it("el pedido de borrado marca lo propio y deja borrar solo eso", async () => {
    const mine = (await reserve(db)).path;
    await upload(db, mine);
    await markDone(db);
    const stranger = "f".repeat(32);
    const theirs = (await reserve(db, { install: stranger, game: other })).path;
    await upload(db, theirs);

    const forget = () => as<{ app_record_forget: string[] }>(db, null, "select public.app_record_forget($1, $2)", [install, secret]);
    expect((await forget()).rows[0].app_record_forget).toEqual([mine]);
    expect((await forget()).rows[0].app_record_forget).toEqual([mine]); // otro pedido las vuelve a dar

    const deleted = await as<{ name: string }>(db, null, "delete from storage.objects returning name");
    expect(deleted.rows).toEqual([{ name: mine }]);

    // Reenviar la misma partida no la vuelve a subir, y la instalación puede seguir compartiendo.
    expect((await reserve(db)).status).toBe("exists");
    expect((await reserve(db, { game: gameId(1) })).status).toBe("new");
  });
});
