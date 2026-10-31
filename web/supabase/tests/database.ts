import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { PGlite } from "@electric-sql/pglite";

/**
 * La migración corre sobre PGlite (Postgres en WebAssembly) con lo mínimo de Supabase: los roles anon y
 * authenticated, auth.users, auth.uid() (que lee el usuario de la sesión) y los permisos por defecto del esquema
 * public. Así se prueban los permisos sin Docker.
 */
const migrations = join(__dirname, "..", "migrations");

export async function database() {
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

export type Db = Awaited<ReturnType<typeof database>>;

export const ana = "11111111-1111-1111-1111-111111111111";
export const beto = "22222222-2222-2222-2222-222222222222";

export async function signUp(db: Db, id: string, name: string) {
  await signUpWith(db, id, { full_name: name });
}

/** Un alta con los datos que mande el proveedor (Discord o Google), tal cual. */
export async function signUpWith(db: Db, id: string, metadata: Record<string, unknown>) {
  await db.query("insert into auth.users (id, raw_user_meta_data) values ($1, $2)", [id, JSON.stringify(metadata)]);
}

/** Corre una consulta como un usuario logueado (o como anónimo si userId es null). */
export async function as<T>(db: Db, userId: string | null, sql: string, params: unknown[] = []) {
  await db.exec(`set role ${userId ? "authenticated" : "anon"}`);
  await db.query("select set_config('request.jwt.claim.sub', $1, false)", [userId ?? ""]);
  try {
    return await db.query<T>(sql, params);
  } finally {
    await db.exec("reset role");
  }
}

export const scouts = {
  schema_version: 1,
  id: "ejemplo-scouts",
  name: "Scouts Feudal",
  civ: "any",
  tags: ["feudal", "scouts"],
  steps: [{ id: "s1", do: "train", what: "villager", count: 6 }],
};

/** Inserta con ese nombre (la base deriva las columnas del JSON, así que el nombre va en el body). */
export const insertBo = `insert into public.build_orders (name, civ, tags, schema_version, body)
                  values ($1, 'any', '{scouts}', 1, jsonb_set($2, '{name}', to_jsonb($1::text))) returning code, owner_id, votes`;
