/**
 * Cada paso descrito en castellano, igual que StepText.Describe del Core (lo que muestran el editor de la app y el
 * simulador de la web). Sirve para escribir los pasos en el HTML desde el servidor, sin esperar al Core en
 * WebAssembly: así los ven los buscadores y quien abre la página antes de que cargue el simulador. Un test compara
 * cada texto con el del Core.
 */

/** Singular y plural de cada entidad (StepText.Names). */
const NAMES: Readonly<Record<string, readonly [string, string]>> = {
  villager: ["aldeano", "aldeanos"],
  militia: ["milicia", "milicias"],
  spearman: ["lancero", "lanceros"],
  archer: ["arquero", "arqueros"],
  skirmisher: ["hostigador", "hostigadores"],
  scout_cavalry: ["explorador", "exploradores"],
  knight: ["caballero", "caballeros"],
  monk: ["monje", "monjes"],
  fishing_ship: ["barco pesquero", "barcos pesqueros"],
  trade_cart: ["carro mercante", "carros mercantes"],
  mangonel: ["mangonel", "mangoneles"],
  eagle_scout: ["explorador águila", "exploradores águila"],
  cavalry_archer: ["arquero a caballo", "arqueros a caballo"],
  hand_cannoneer: ["artillero manual", "artilleros manuales"],
  camel_rider: ["camello", "camellos"],
  battle_elephant: ["elefante de combate", "elefantes de combate"],
  steppe_lancer: ["lancero estepario", "lanceros esteparios"],
  galley: ["galera", "galeras"],
  fire_galley: ["galeota incendiaria", "galeotas incendiarias"],
  demolition_raft: ["balsa de demolición", "balsas de demolición"],
  transport_ship: ["barco de transporte", "barcos de transporte"],
  battering_ram: ["ariete", "arietes"],
  scorpion: ["escorpión", "escorpiones"],
  town_center: ["centro urbano", "centros urbanos"],
  house: ["casa", "casas"],
  mill: ["molino", "molinos"],
  lumber_camp: ["campamento maderero", "campamentos madereros"],
  mining_camp: ["campamento minero", "campamentos mineros"],
  settlement: ["asentamiento", "asentamientos"],
  mule_cart: ["carro de mulas", "carros de mulas"],
  farm: ["granja", "granjas"],
  dock: ["muelle", "muelles"],
  barracks: ["cuartel", "cuarteles"],
  archery_range: ["galería de tiro", "galerías de tiro"],
  stable: ["establo", "establos"],
  blacksmith: ["herrería", "herrerías"],
  market: ["mercado", "mercados"],
  monastery: ["monasterio", "monasterios"],
  university: ["universidad", "universidades"],
  siege_workshop: ["taller de asedio", "talleres de asedio"],
  palisade_wall: ["empalizada", "empalizadas"],
  stone_wall: ["muralla de piedra", "murallas de piedra"],
  outpost: ["puesto avanzado", "puestos avanzados"],
  watch_tower: ["torre de vigilancia", "torres de vigilancia"],
  castle: ["castillo", "castillos"],
  loom: ["Telar", "Telar"],
  town_watch: ["Guardia urbana", "Guardia urbana"],
  wheelbarrow: ["Carretilla", "Carretilla"],
  hand_cart: ["Carro de mano", "Carro de mano"],
  double_bit_axe: ["Hacha de doble filo", "Hacha de doble filo"],
  bow_saw: ["Sierra de arco", "Sierra de arco"],
  horse_collar: ["Collera", "Collera"],
  heavy_plow: ["Arado pesado", "Arado pesado"],
  gold_mining: ["Minería de oro", "Minería de oro"],
  gold_shaft_mining: ["Pozo de oro", "Pozo de oro"],
  stone_mining: ["Minería de piedra", "Minería de piedra"],
  stone_shaft_mining: ["Pozo de piedra", "Pozo de piedra"],
  feudal_age: ["Edad Feudal", "Edad Feudal"],
  castle_age: ["Edad de los Castillos", "Edad de los Castillos"],
  imperial_age: ["Edad Imperial", "Edad Imperial"],
  man_at_arms: ["Hombre de armas", "Hombre de armas"],
  crossbowman: ["Ballestero", "Ballestero"],
  light_cavalry: ["Caballería ligera", "Caballería ligera"],
  fletching: ["Emplumado", "Emplumado"],
  forging: ["Forja", "Forja"],
  scale_mail_armor: ["Cota de escamas", "Cota de escamas"],
  scale_barding_armor: ["Barda de escamas", "Barda de escamas"],
  padded_archer_armor: ["Armadura acolchada", "Armadura acolchada"],
  bloodlines: ["Linaje", "Linaje"],
  husbandry: ["Cría de caballos", "Cría de caballos"],
  coinage: ["Acuñación", "Acuñación"],
};

const SOURCES: Readonly<Record<string, string>> = {
  sheep: "ovejas",
  hunt: "caza",
  berries: "bayas",
  fish: "pesca",
  farm: "granjas",
  wood: "madera",
  gold: "oro",
  stone: "piedra",
};

const ACTIONS: Readonly<Record<string, string>> = { train: "Entrenar", build: "Construir", research: "Investigar" };

/** Lo que hace falta de un paso de la BO (el JSON de la app). */
export interface StepLike {
  do?: unknown;
  what?: unknown;
  count?: unknown;
  assign?: unknown;
  from?: unknown;
  builders?: unknown;
  at_pop?: unknown;
}

/** Nombre en castellano de una entidad; si no hay traducción, la clave con espacios. */
export function entityName(key: string, count = 1) {
  const names = Object.hasOwn(NAMES, key) ? NAMES[key] : null;
  return names ? (count === 1 ? names[0] : names[1]) : key.replaceAll("_", " ");
}

function sourceName(source: unknown) {
  return typeof source === "string" && Object.hasOwn(SOURCES, source) ? SOURCES[source] : String(source);
}

const text = (value: unknown) => (typeof value === "string" ? value : null);
const integer = (value: unknown) => (typeof value === "number" && Number.isInteger(value) ? value : null);

/** Una línea que describe el paso, por ejemplo "6 aldeanos en ovejas" o "Construir 1 cuartel". */
export function describeStep(step: StepLike): string {
  const count = integer(step.count) ?? 1;
  const assign = text(step.assign);
  const what = text(step.what);

  if (step.do === "move") {
    const to = assign ? sourceName(assign) : "?";
    const from = text(step.from);
    if (!from) return `Mandar ${count} ${count === 1 ? "aldeano sin tarea" : "aldeanos sin tarea"} a ${to}`;
    return `Mover ${count} de ${sourceName(from)} a ${to}`;
  }

  // La cantidad de un paso de aldeanos es el total en la fuente, como el contador del HUD.
  if (step.do === "train" && what === "villager" && assign) return `${count} ${entityName("villager", count)} en ${sourceName(assign)}`;

  const name = what === null ? "?" : entityName(what, count);
  const action = typeof step.do === "string" && Object.hasOwn(ACTIONS, step.do) ? ACTIONS[step.do] : "Mover";
  let line = step.do === "research" ? `Investigar ${name}` : `${action} ${count} ${name}`;
  const builders = integer(step.builders);
  if (step.do === "build" && builders !== null && builders > 1) line += ` (${builders} constructores)`;
  if (assign) line += ` → ${sourceName(assign)}`;
  const atPop = integer(step.at_pop);
  if (atPop !== null) line += ` a ${atPop} de población`;
  return line;
}
