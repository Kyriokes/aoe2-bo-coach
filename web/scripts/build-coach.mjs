// Compila el Core de C# a WebAssembly (src/BoCoach.Wasm) y lo deja en public/coach junto con los datos que usa:
//
//   public/coach/_framework/   runtime de .NET y los ensamblados (dotnet.js es la entrada)
//   public/coach/game_data.json   data/game_data de la versión más alta del .dat
//   public/coach/events.json      data/events.json
//   public/coach/ejemplos/        las BO de build_orders/ e index.json (nombre de cada una)
//
// Corre solo antes de `next build` y `next dev` (prebuild/predev). Necesita el SDK de .NET (ver global.json).

import { cpSync, existsSync, mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { execFileSync } from "node:child_process";

const web = join(dirname(fileURLToPath(import.meta.url)), "..");
const repo = join(web, "..");
const out = join(web, "public", "coach");
const publish = join(tmpdir(), `bocoach-wasm-${process.pid}`);

const dotnet = process.env.DOTNET_ROOT ? join(process.env.DOTNET_ROOT, "dotnet") : "dotnet";
try {
  execFileSync(dotnet, ["--version"], { stdio: "ignore" });
} catch {
  console.error("No encuentro el SDK de .NET: hace falta para compilar el Core a WebAssembly (ver global.json).");
  process.exit(1);
}

console.log("Compilando src/BoCoach.Wasm…");
execFileSync(dotnet, ["publish", join(repo, "src", "BoCoach.Wasm"), "-c", "Release", "-o", publish, "-v", "q", "--nologo"], {
  stdio: "inherit",
});

rmSync(out, { recursive: true, force: true });
mkdirSync(out, { recursive: true });
// Sin las copias .br y .gz que deja dotnet publish: el navegador pide los archivos tal cual y Vercel los comprime
// al servirlos, así que solo agrandaban el deploy.
cpSync(join(publish, "wwwroot", "_framework"), join(out, "_framework"), {
  recursive: true,
  filter: (source) => !/\.(br|gz)$/.test(source),
});
rmSync(publish, { recursive: true, force: true });

// game_data: la versión más alta (v8.9.json, v8.10.json, ...), como GameDataLoader.LoadLatest.
const gameData = readdirSync(join(repo, "data", "game_data"))
  .map((file) => ({ file, version: /^v(\d+(?:\.\d+)*)\.json$/.exec(file)?.[1] }))
  .filter((f) => f.version)
  .sort((a, b) => compareVersions(b.version, a.version))[0];
if (!gameData) throw new Error("No hay archivos en data/game_data.");
cpSync(join(repo, "data", "game_data", gameData.file), join(out, "game_data.json"));
cpSync(join(repo, "data", "events.json"), join(out, "events.json"));

// BO de ejemplo (las mismas que trae la app), para probar "Subir una BO" sin tener una propia.
const examples = join(out, "ejemplos");
mkdirSync(examples, { recursive: true });
const index = readdirSync(join(repo, "build_orders"))
  .filter((file) => file.endsWith(".json"))
  .map((file) => {
    cpSync(join(repo, "build_orders", file), join(examples, file));
    return { file, name: JSON.parse(readFileSync(join(repo, "build_orders", file), "utf8")).name };
  });
writeFileSync(join(examples, "index.json"), JSON.stringify(index, null, 2));

console.log(`Core en WebAssembly listo en public/coach (${gameData.file}, ${index.length} BO de ejemplo).`);

function compareVersions(a, b) {
  const pa = a.split(".").map(Number);
  const pb = b.split(".").map(Number);
  for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
    const d = (pa[i] ?? 0) - (pb[i] ?? 0);
    if (d !== 0) return d;
  }
  return 0;
}

if (!existsSync(join(out, "_framework", "dotnet.js"))) throw new Error("Falta _framework/dotnet.js en la salida.");
