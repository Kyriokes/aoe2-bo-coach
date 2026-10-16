import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { PGlite } from "@electric-sql/pglite";
import { beforeEach, describe, expect, it } from "vitest";

/**
 * La migración corre sobre PGlite (Postgres en WebAssembly) con lo mínimo de Supabase: los roles anon y
 * authenticated, auth.users, auth.uid() (que lee el usuario de la sesión) y los permisos por defecto del esquema
 * public. Así se prueban los permisos sin Docker.
 */
const migrations = join(__dirname, "..", "migrations");

async function database() {
  const db = new PGlite();
  await db.exec(`
    create role anon nologin;
    create role authenticated nologin;
    create schema auth;
    create table auth.users (id uuid primary key, raw_user_meta_data jsonb not null default '{}');
    create function auth.uid() returns uuid language sql stable as $$
      select nullif(current_setting('request.jwt.claim.sub', true), '')::uuid
    $$;
    grant usage on schema public, auth to anon, authenticated;
    grant execute on function auth.uid() to anon, authenticated;
    alter default privileges in schema public grant all on tables to anon, authenticated;
    alter default privileges in schema public grant all on functions to anon, authenticated;
  `);
  for (const file of readdirSync(migrations).filter((f) => f.endsWith(".sql")).sort()) {
    await db.exec(readFileSync(join(migrations, file), "utf8"));
  }
  return db;
}

type Db = Awaited<ReturnType<typeof database>>;

const ana = "11111111-1111-1111-1111-111111111111";
const beto = "22222222-2222-2222-2222-222222222222";

async function signUp(db: Db, id: string, name: string) {
  await db.query("insert into auth.users (id, raw_user_meta_data) values ($1, $2)", [id, JSON.stringify({ full_name: name })]);
}

/** Corre una consulta como un usuario logueado (o como anónimo si userId es null). */
async function as<T>(db: Db, userId: string | null, sql: string, params: unknown[] = []) {
  await db.exec(`set role ${userId ? "authenticated" : "anon"}`);
  await db.query("select set_config('request.jwt.claim.sub', $1, false)", [userId ?? ""]);
  try {
    return await db.query<T>(sql, params);
  } finally {
    await db.exec("reset role");
  }
}

const scouts = {
  schema_version: 1,
  id: "ejemplo-scouts",
  name: "Scouts Feudal",
  civ: "any",
  tags: ["feudal", "scouts"],
  steps: [{ id: "s1", do: "train", what: "villager", count: 6 }],
};

const insertBo = `insert into public.build_orders (name, civ, tags, schema_version, body)
                  values ($1, 'any', '{scouts}', 1, $2) returning code, owner_id, votes`;

describe("biblioteca", () => {
  let db: Db;

  beforeEach(async () => {
    db = await database();
    await signUp(db, ana, "Ana");
    await signUp(db, beto, "Beto");
  });

  it("al registrarse se crea el perfil con el nombre de Discord o Google", async () => {
    const { rows } = await db.query<{ display_name: string }>("select display_name from public.profiles order by display_name");
    expect(rows.map((r) => r.display_name)).toEqual(["Ana", "Beto"]);
  });

  it("cualquiera ve las BO, pero publicar pide cuenta", async () => {
    await expect(as(db, null, insertBo, ["x", scouts])).rejects.toThrow();

    const { rows } = await as<{ code: string; owner_id: string; votes: number }>(db, ana, insertBo, ["Scouts", scouts]);
    expect(rows[0].owner_id).toBe(ana);
    expect(rows[0].code).toMatch(/^[a-z2-9]{6}$/);

    const visible = await as<{ name: string }>(db, null, "select name from public.build_orders");
    expect(visible.rows).toEqual([{ name: "Scouts" }]);
  });

  it("cada uno edita y borra solo lo suyo, y no puede tocar los votos", async () => {
    await as(db, ana, insertBo, ["Scouts", scouts]);

    const byBeto = await as(db, beto, "update public.build_orders set name = 'Robada' returning name");
    expect(byBeto.rows).toEqual([]);
    const deleted = await as(db, beto, "delete from public.build_orders returning name");
    expect(deleted.rows).toEqual([]);
    await expect(as(db, ana, "update public.build_orders set votes = 1000")).rejects.toThrow(/permission/);

    const byAna = await as<{ name: string }>(db, ana, "update public.build_orders set name = 'Scouts 2' returning name");
    expect(byAna.rows).toEqual([{ name: "Scouts 2" }]);
  });

  it("una BO sin pasos no se guarda", async () => {
    await expect(as(db, ana, insertBo, ["Vacía", { schema_version: 1, name: "Vacía" }])).rejects.toThrow(/check/);
  });

  it("un voto por usuario, con el contador al día", async () => {
    const { rows } = await as<{ code: string }>(db, ana, insertBo, ["Scouts", scouts]);
    const id = (await db.query<{ id: string }>("select id from public.build_orders where code = $1", [rows[0].code])).rows[0].id;
    const vote = "insert into public.votes (build_order_id) values ($1)";

    await as(db, ana, vote, [id]);
    await as(db, beto, vote, [id]);
    await expect(as(db, beto, vote, [id])).rejects.toThrow(/duplicate/);
    await expect(as(db, beto, "insert into public.votes (user_id, build_order_id) values ($1, $2)", [ana, id])).rejects.toThrow();
    expect((await db.query<{ votes: number }>("select votes from public.build_orders")).rows[0].votes).toBe(2);

    await as(db, beto, "delete from public.votes where build_order_id = $1", [id]);
    expect((await db.query<{ votes: number }>("select votes from public.build_orders")).rows[0].votes).toBe(1);
  });

  describe("publicar desde la app", () => {
    async function token(userId: string) {
      const { rows } = await as<{ token: string }>(db, userId, "select public.create_app_token('Mi PC') as token");
      return rows[0].token;
    }

    const publish = "select public.app_publish($1, $2, $3) as code";
    const update = "select public.app_publish($1, $2, null, null, $3) as code";

    it("el token se ve una sola vez: en la base queda solo el hash", async () => {
      const t = await token(ana);
      expect(t).toMatch(/^bocoach_[0-9a-f]{64}$/);

      const stored = await as<{ token_hash: string; label: string }>(db, ana, "select token_hash, label from public.app_tokens");
      expect(stored.rows).toHaveLength(1);
      expect(stored.rows[0].token_hash).not.toContain(t);
      expect(stored.rows[0].label).toBe("Mi PC");
      expect((await as(db, beto, "select * from public.app_tokens")).rows).toEqual([]);
    });

    it("sin sesión no se pueden crear tokens", async () => {
      await expect(as(db, null, "select public.create_app_token()")).rejects.toThrow();
    });

    it("la app publica con el token y la BO queda a nombre del dueño", async () => {
      const t = await token(ana);

      const { rows } = await as<{ code: string }>(db, null, publish, [t, scouts, 475]);

      const bo = await db.query<{ owner_id: string; name: string; tags: string[]; feudal_click: number }>(
        "select owner_id, name, tags, feudal_click from public.build_orders where code = $1", [rows[0].code]);
      expect(bo.rows[0]).toEqual({ owner_id: ana, name: "Scouts Feudal", tags: ["feudal", "scouts"], feudal_click: 475 });
    });

    it("con un token inválido no publica", async () => {
      await expect(as(db, null, publish, ["bocoach_falso", scouts, null])).rejects.toThrow(/token/);
    });

    it("actualiza solo las BO propias", async () => {
      const anaToken = await token(ana);
      const betoToken = await token(beto);
      const code = (await as<{ code: string }>(db, null, publish, [anaToken, scouts, null])).rows[0].code;

      await expect(as(db, null, update, [betoToken, { ...scouts, name: "Robada" }, code])).rejects.toThrow(/código/);

      await as(db, null, update, [anaToken, { ...scouts, name: "Scouts v2" }, code]);
      const name = await db.query<{ name: string }>("select name from public.build_orders where code = $1", [code]);
      expect(name.rows[0].name).toBe("Scouts v2");
    });

    it("las funciones internas no se pueden llamar desde afuera", async () => {
      await expect(as(db, null, "select public.app_token_owner('x')")).rejects.toThrow(/permission/);
    });
  });
});
