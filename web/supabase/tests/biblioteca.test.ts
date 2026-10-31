import { beforeEach, describe, expect, it } from "vitest";
import { ana, as, beto, database, type Db, insertBo, scouts, signUp } from "./database";


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

    const byAna = await as<{ name: string }>(db, ana, `update public.build_orders set body = body || '{"name": "Scouts 2"}' returning name`);
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
      await expect(as(db, null, "select public.hash_token('x')")).rejects.toThrow(/permission/);
    });

    it("con el token tampoco se saltean los topes de la BO", async () => {
      const t = await token(ana);
      const huge = { ...scouts, steps: [{ id: "s1", do: "train", what: "villager", count: 1_000_000 }] };
      await expect(as(db, null, publish, [t, huge, null])).rejects.toThrow(/build_orders_body_ok/);
    });

    it("hasta 20 tokens por cuenta", async () => {
      for (let i = 0; i < 20; i++) await token(ana);
      await expect(token(ana)).rejects.toThrow(/20 tokens/);
    });
  });

  describe("topes aunque se le hable directo a la base", () => {
    const bad = (body: unknown) => expect(as(db, ana, insertBo, ["Mala", body])).rejects.toThrow(/check/);
    const step = (extra: Record<string, unknown>) => ({ ...scouts, steps: [{ id: "s1", do: "train", what: "villager", ...extra }] });

    it("count y builders de 1 a 200, enteros", async () => {
      await as(db, ana, insertBo, ["Bien", step({ count: 200 })]);
      await as(db, ana, insertBo, ["Sin count", step({})]);
      await as(db, ana, insertBo, ["Builders null", step({ do: "build", what: "house", builders: null })]);
      await bad(step({ count: 201 }));
      await bad(step({ count: 0 }));
      await bad(step({ count: 2.5 }));
      await bad(step({ count: "mucho" }));
      await bad(step({ do: "build", what: "house", builders: 100000 }));
    });

    it("los pasos de las ramas también", async () => {
      const branch = (count: number) => ({ ...scouts, branches: [{ on: "enemy_feudal_click", steps: [{ id: "r1", do: "train", what: "archer", count }] }] });
      await as(db, ana, insertBo, ["Rama", branch(6)]);
      await bad(branch(999999));
      await bad({ ...scouts, branches: "no" });
    });

    it("hasta 300 pasos y 512 KB", async () => {
      const many = (n: number) => ({ ...scouts, steps: Array.from({ length: n }, (_, i) => ({ id: `s${i}`, do: "train", what: "villager" })) });
      await as(db, ana, insertBo, ["300 pasos", many(300)]);
      await bad(many(301));
      await bad({ ...scouts, description: "x".repeat(600_000) });
      await bad({ ...scouts, steps: ["no es un paso"] });
    });

    it("etiquetas cortas, versión del formato y clics de edad razonables", async () => {
      await bad({ ...scouts, tags: ["x".repeat(31)] });
      await bad({ ...scouts, schema_version: 1000 });
      await expect(as(db, ana, "insert into public.build_orders (name, schema_version, body, feudal_click) values ('x', 1, $1, -5)", [scouts])).rejects.toThrow(/check/);
    });

    it("las columnas salen del JSON, y el autor tiene hasta 60 caracteres", async () => {
      const body = { ...scouts, civ: " Franks ", tags: [" fc ", ""], description: "  ", author: "Hera" };
      const direct = "insert into public.build_orders (name, civ, tags, schema_version, body) values ('Otra', 'britons', '{x}', 7, $1) returning code";
      const { rows } = await as<{ code: string }>(db, ana, direct, [body]);
      const columns = await db.query("select name, civ, tags, description, schema_version from public.build_orders where code = $1", [rows[0].code]);
      expect(columns.rows[0]).toEqual({ name: "Scouts Feudal", civ: "Franks", tags: ["fc"], description: null, schema_version: 1 });

      await bad({ ...scouts, author: "x".repeat(61) });
      await expect(as(db, ana, direct, [{ ...scouts, name: "   " }])).rejects.toThrow(/nombre/);
      await expect(as(db, ana, direct, [{ ...scouts, schema_version: "1" }])).rejects.toThrow(/schema_version/);
      await expect(as(db, ana, direct, [{ ...scouts, schema_version: 1.5 }])).rejects.toThrow(/schema_version/);
    });

    it("hasta 150 BO nuevas por cuenta en 24 horas", async () => {
      await db.query(
        `insert into public.build_orders (owner_id, name, schema_version, body)
         select $1, 'BO ' || i, 1, $2 from generate_series(1, 150) as i`, [ana, scouts]);
      await expect(as(db, ana, insertBo, ["La 151", scouts])).rejects.toThrow(/24 horas/);
      await as(db, beto, insertBo, ["La de Beto", scouts]);
    });
  });
});

describe("privadas y suscripciones", () => {
  let db: Db;

  beforeEach(async () => {
    db = await database();
    await signUp(db, ana, "Ana");
    await signUp(db, beto, "Beto");
  });

  const insertWith = `insert into public.build_orders (name, schema_version, body, visibility)
                      values ($1, 1, jsonb_set($2, '{name}', to_jsonb($1::text)), $3) returning id, code`;

  async function publishAs(userId: string, name: string, visibility: "public" | "private", body: object = scouts) {
    return (await as<{ id: string; code: string }>(db, userId, insertWith, [name, body, visibility])).rows[0];
  }

  async function token(userId: string) {
    return (await as<{ token: string }>(db, userId, "select public.create_app_token('PC') as token")).rows[0].token;
  }

  const names = async (userId: string | null) =>
    (await as<{ name: string }>(db, userId, "select name from public.build_orders order by name")).rows.map((r) => r.name);

  it("una privada solo la ve su dueña", async () => {
    await publishAs(ana, "Privada", "private");
    await publishAs(ana, "Pública", "public");

    expect(await names(null)).toEqual(["Pública"]);
    expect(await names(beto)).toEqual(["Pública"]);
    expect(await names(ana)).toEqual(["Privada", "Pública"]);
  });

  it("lo publicado sin decir nada queda público", async () => {
    await as(db, ana, insertBo, ["Scouts", scouts]);
    expect((await db.query<{ visibility: string }>("select visibility from public.build_orders")).rows).toEqual([{ visibility: "public" }]);
  });

  it("no se vota una privada ajena y cada uno cambia la visibilidad de las suyas", async () => {
    const privada = await publishAs(ana, "Privada", "private");
    const vote = "insert into public.votes (build_order_id) values ($1)";
    await expect(as(db, beto, vote, [privada.id])).rejects.toThrow();

    const byBeto = await as(db, beto, "update public.build_orders set visibility = 'public' returning code");
    expect(byBeto.rows).toEqual([]);
    const byAna = await as<{ visibility: string }>(db, ana, "update public.build_orders set visibility = 'public' returning visibility");
    expect(byAna.rows).toEqual([{ visibility: "public" }]);
    await expect(as(db, ana, "update public.build_orders set visibility = 'secreta'")).rejects.toThrow(/check/);
  });

  it("se suscribe solo a públicas ajenas y cada uno ve las suyas", async () => {
    const publica = await publishAs(ana, "Pública", "public");
    const privada = await publishAs(ana, "Privada", "private");
    const propia = await publishAs(beto, "De Beto", "public");
    const subscribe = "insert into public.subscriptions (build_order_id) values ($1)";

    await as(db, beto, subscribe, [publica.id]);
    await expect(as(db, beto, subscribe, [privada.id])).rejects.toThrow();
    await expect(as(db, beto, subscribe, [propia.id])).rejects.toThrow();
    await expect(as(db, null, subscribe, [publica.id])).rejects.toThrow();

    expect((await as(db, ana, "select * from public.subscriptions")).rows).toEqual([]);
    expect((await as(db, ana, "delete from public.subscriptions returning user_id")).rows).toEqual([]);
    expect((await as(db, beto, "select build_order_id from public.subscriptions")).rows).toEqual([{ build_order_id: publica.id }]);
  });

  it("al borrar la BO se van sus suscripciones", async () => {
    const publica = await publishAs(ana, "Pública", "public");
    await as(db, beto, "insert into public.subscriptions (build_order_id) values ($1)", [publica.id]);

    await as(db, ana, "delete from public.build_orders");
    expect((await db.query("select * from public.subscriptions")).rows).toEqual([]);
  });

  it("el mapa y el estilo salen del JSON", async () => {
    const scoutsOnArabia = { ...scouts, map: "arabia", steps: [...scouts.steps, { id: "s2", do: "train", what: "scout_cavalry", count: 3 }] };
    const bo = await publishAs(ana, "Scouts", "public", scoutsOnArabia);
    const read = async () =>
      (await db.query<{ map: string | null; style: string | null }>("select map, style from public.build_orders where id = $1", [bo.id])).rows[0];
    expect(await read()).toEqual({ map: "arabia", style: "scouts" });

    await as(db, ana, "update public.build_orders set body = $1", [scouts]);
    expect(await read()).toEqual({ map: null, style: null });
  });

  describe("con el token de la app", () => {
    type Item = { code: string; name: string; own: boolean; visibility: string; author: string; guide_author: string };
    const mine = async (t: string) =>
      (await as<{ result: { account: string; items: Item[] } }>(db, null, "select public.app_my_build_orders($1) as result", [t])).rows[0].result;
    const list = async (t: string) => (await mine(t)).items;
    const subscribe = (t: string, code: string) =>
      as<{ result: string }>(db, null, "select public.app_subscribe($1, $2) as result", [t, code]);

    it("la lista trae las propias y las suscriptas públicas", async () => {
      const t = await token(beto);
      await publishAs(beto, "B privada", "private");
      const publica = await publishAs(ana, "A pública", "public", { ...scouts, author: "Cicero" });
      const despues = await publishAs(ana, "A después privada", "public");
      await subscribe(t, publica.code);
      await subscribe(t, despues.code);
      await as(db, ana, "update public.build_orders set visibility = 'private' where id = $1", [despues.id]);

      const items = await list(t);
      expect(items.map((i) => [i.name, i.own, i.visibility])).toEqual([
        ["B privada", true, "private"],
        ["A pública", false, "public"],
      ]);
      expect(items[1]).toMatchObject({ author: "Ana", guide_author: "Cicero" });
    });

    it("trae el nombre de la cuenta, también sin BO", async () => {
      expect(await mine(await token(beto))).toEqual({ account: "Beto", items: [] });
    });

    it("con un token inválido no da nada", async () => {
      await expect(list("bocoach_falso")).rejects.toThrow(/token/);
    });

    it("suscribirse: propia, pública ajena, privada ajena y código que no existe", async () => {
      const t = await token(beto);
      const propia = await publishAs(beto, "Mía", "public");
      const publica = await publishAs(ana, "Pública", "public");
      const privada = await publishAs(ana, "Privada", "private");

      expect((await subscribe(t, propia.code)).rows[0].result).toBe("own");
      expect((await subscribe(t, publica.code)).rows[0].result).toBe("subscribed");
      expect((await subscribe(t, publica.code)).rows[0].result).toBe("subscribed");
      await expect(subscribe(t, privada.code)).rejects.toThrow(/pública/);
      await expect(subscribe(t, "zzzzzz")).rejects.toThrow(/pública/);
      expect((await db.query("select * from public.subscriptions")).rows).toHaveLength(1);

      await as(db, null, "select public.app_unsubscribe($1, $2)", [t, publica.code]);
      await as(db, null, "select public.app_unsubscribe($1, $2)", [t, publica.code]);
      expect((await db.query("select * from public.subscriptions")).rows).toEqual([]);
    });

    it("baja una privada propia pero no una ajena", async () => {
      const t = await token(beto);
      const mia = await publishAs(beto, "Mía", "private");
      const ajena = await publishAs(ana, "Ajena", "private");
      const get = async (code: string) =>
        (await as<{ bo: Record<string, unknown> | null }>(db, null, "select public.app_build_order($1, $2) as bo", [t, code])).rows[0].bo;

      expect(await get(mia.code)).toMatchObject({ code: mia.code, name: "Mía", author: "Beto", build_order: { name: "Mía" } });
      expect(await get(ajena.code)).toBeNull();
    });

    it("reemplazar desde la app no cambia la visibilidad", async () => {
      const t = await token(ana);
      const privada = await publishAs(ana, "Privada", "private");
      await as(db, null, "select public.app_publish($1, $2, null, null, $3)", [t, { ...scouts, name: "Nueva" }, privada.code]);

      expect((await db.query<{ visibility: string }>("select visibility from public.build_orders")).rows).toEqual([{ visibility: "private" }]);
    });
  });
});
