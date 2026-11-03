import type { Locale } from "@/i18n/locales";

/**
 * Cada paso descrito en el idioma de la página, igual que StepText.Describe del Core (lo que muestran el editor de la
 * app y el simulador de la web). Sirve para escribir los pasos en el HTML desde el servidor, sin esperar al Core en
 * WebAssembly: así los ven los buscadores y quien abre la página antes de que cargue el simulador. Un test compara
 * cada texto con el del Core en los dos idiomas: si se cambia uno, se cambia el otro.
 */

type Names = Readonly<Record<string, readonly [string, string]>>;

/** Singular y plural de cada entidad (StepText.NamesEs). */
const NAMES_ES: Names = {
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
  crop_rotation: ["Rotación de cultivos", "Rotación de cultivos"],
  two_man_saw: ["Sierra de dos manos", "Sierra de dos manos"],
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
  thumb_ring: ["Anillo del pulgar", "Anillo del pulgar"],
  parthian_tactics: ["Tácticas partas", "Tácticas partas"],
  heavy_cavalry_archer: ["Arquero a caballo pesado", "Arquero a caballo pesado"],
  bodkin_arrow: ["Flecha punzante", "Flecha punzante"],
  leather_archer_armor: ["Armadura de cuero para arqueros", "Armadura de cuero para arqueros"],
  ballistics: ["Balística", "Balística"],
  coinage: ["Acuñación", "Acuñación"],
};

/** StepText.NamesEn: los nombres del juego en inglés. */
const NAMES_EN: Names = {
  villager: ["villager", "villagers"],
  militia: ["militia", "militia"],
  spearman: ["spearman", "spearmen"],
  archer: ["archer", "archers"],
  skirmisher: ["skirmisher", "skirmishers"],
  scout_cavalry: ["scout", "scouts"],
  knight: ["knight", "knights"],
  monk: ["monk", "monks"],
  fishing_ship: ["fishing ship", "fishing ships"],
  trade_cart: ["trade cart", "trade carts"],
  mangonel: ["mangonel", "mangonels"],
  eagle_scout: ["eagle scout", "eagle scouts"],
  cavalry_archer: ["cavalry archer", "cavalry archers"],
  hand_cannoneer: ["hand cannoneer", "hand cannoneers"],
  camel_rider: ["camel rider", "camel riders"],
  battle_elephant: ["battle elephant", "battle elephants"],
  steppe_lancer: ["steppe lancer", "steppe lancers"],
  galley: ["galley", "galleys"],
  fire_galley: ["fire galley", "fire galleys"],
  demolition_raft: ["demolition raft", "demolition rafts"],
  transport_ship: ["transport ship", "transport ships"],
  battering_ram: ["battering ram", "battering rams"],
  scorpion: ["scorpion", "scorpions"],
  town_center: ["town center", "town centers"],
  house: ["house", "houses"],
  mill: ["mill", "mills"],
  lumber_camp: ["lumber camp", "lumber camps"],
  mining_camp: ["mining camp", "mining camps"],
  settlement: ["settlement", "settlements"],
  mule_cart: ["mule cart", "mule carts"],
  farm: ["farm", "farms"],
  dock: ["dock", "docks"],
  barracks: ["barracks", "barracks"],
  archery_range: ["archery range", "archery ranges"],
  stable: ["stable", "stables"],
  blacksmith: ["blacksmith", "blacksmiths"],
  market: ["market", "markets"],
  monastery: ["monastery", "monasteries"],
  university: ["university", "universities"],
  siege_workshop: ["siege workshop", "siege workshops"],
  palisade_wall: ["palisade wall", "palisade walls"],
  stone_wall: ["stone wall", "stone walls"],
  outpost: ["outpost", "outposts"],
  watch_tower: ["watch tower", "watch towers"],
  castle: ["castle", "castles"],
  loom: ["Loom", "Loom"],
  town_watch: ["Town Watch", "Town Watch"],
  wheelbarrow: ["Wheelbarrow", "Wheelbarrow"],
  hand_cart: ["Hand Cart", "Hand Cart"],
  double_bit_axe: ["Double-Bit Axe", "Double-Bit Axe"],
  bow_saw: ["Bow Saw", "Bow Saw"],
  horse_collar: ["Horse Collar", "Horse Collar"],
  heavy_plow: ["Heavy Plow", "Heavy Plow"],
  crop_rotation: ["Crop Rotation", "Crop Rotation"],
  two_man_saw: ["Two-Man Saw", "Two-Man Saw"],
  gold_mining: ["Gold Mining", "Gold Mining"],
  gold_shaft_mining: ["Gold Shaft Mining", "Gold Shaft Mining"],
  stone_mining: ["Stone Mining", "Stone Mining"],
  stone_shaft_mining: ["Stone Shaft Mining", "Stone Shaft Mining"],
  feudal_age: ["Feudal Age", "Feudal Age"],
  castle_age: ["Castle Age", "Castle Age"],
  imperial_age: ["Imperial Age", "Imperial Age"],
  man_at_arms: ["Man-at-Arms", "Man-at-Arms"],
  crossbowman: ["Crossbowman", "Crossbowman"],
  light_cavalry: ["Light Cavalry", "Light Cavalry"],
  fletching: ["Fletching", "Fletching"],
  forging: ["Forging", "Forging"],
  scale_mail_armor: ["Scale Mail Armor", "Scale Mail Armor"],
  scale_barding_armor: ["Scale Barding Armor", "Scale Barding Armor"],
  padded_archer_armor: ["Padded Archer Armor", "Padded Archer Armor"],
  bloodlines: ["Bloodlines", "Bloodlines"],
  husbandry: ["Husbandry", "Husbandry"],
  thumb_ring: ["Thumb Ring", "Thumb Ring"],
  parthian_tactics: ["Parthian Tactics", "Parthian Tactics"],
  heavy_cavalry_archer: ["Heavy Cavalry Archer", "Heavy Cavalry Archer"],
  bodkin_arrow: ["Bodkin Arrow", "Bodkin Arrow"],
  leather_archer_armor: ["Leather Archer Armor", "Leather Archer Armor"],
  ballistics: ["Ballistics", "Ballistics"],
  coinage: ["Coinage", "Coinage"],
};

const NAMES: Readonly<Record<Locale, Names>> = { es: NAMES_ES, en: NAMES_EN };

const SOURCES: Readonly<Record<Locale, Readonly<Record<string, string>>>> = {
  es: { sheep: "ovejas", hunt: "caza", berries: "bayas", fish: "pesca", farm: "granjas", wood: "madera", gold: "oro", stone: "piedra" },
  en: { sheep: "sheep", hunt: "hunt", berries: "berries", fish: "fish", farm: "farms", wood: "wood", gold: "gold", stone: "stone" },
};

const ACTIONS: Readonly<Record<Locale, Readonly<Record<string, string>>>> = {
  es: { train: "Entrenar", build: "Construir", research: "Investigar", move: "Mover" },
  en: { train: "Train", build: "Build", research: "Research", move: "Move" },
};

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

/** Si la entidad tiene nombre en ese idioma (un test lo exige para todo game_data). */
export function hasEntityName(key: string, locale: Locale = "es") {
  return Object.hasOwn(NAMES[locale], key);
}

/** Nombre de una entidad en el idioma; si no hay traducción, la clave con espacios. */
export function entityName(key: string, count = 1, locale: Locale = "es") {
  const names = hasEntityName(key, locale) ? NAMES[locale][key] : null;
  return names ? (count === 1 ? names[0] : names[1]) : key.replaceAll("_", " ");
}

/** "ovejas", "madera"... a partir de la clave de la fuente. */
export function sourceName(source: unknown, locale: Locale = "es") {
  return typeof source === "string" && Object.hasOwn(SOURCES[locale], source) ? SOURCES[locale][source] : String(source);
}

const text = (value: unknown) => (typeof value === "string" ? value : null);
const integer = (value: unknown) => (typeof value === "number" && Number.isInteger(value) ? value : null);

/** Una línea que describe el paso, por ejemplo "6 aldeanos en ovejas" o "Build 1 barracks". */
export function describeStep(step: StepLike, locale: Locale = "es"): string {
  const en = locale === "en";
  const count = integer(step.count) ?? 1;
  const assign = text(step.assign);
  const what = text(step.what);

  if (step.do === "move") {
    const to = assign ? sourceName(assign, locale) : "?";
    const from = text(step.from);
    if (!from) {
      return en
        ? `Send ${count} idle ${count === 1 ? "villager" : "villagers"} to ${to}`
        : `Mandar ${count} ${count === 1 ? "aldeano sin tarea" : "aldeanos sin tarea"} a ${to}`;
    }
    return en ? `Move ${count} from ${sourceName(from, locale)} to ${to}` : `Mover ${count} de ${sourceName(from, locale)} a ${to}`;
  }

  // La cantidad de un paso de aldeanos es el total en la fuente, como el contador del HUD.
  if (step.do === "train" && what === "villager" && assign) {
    return `${count} ${entityName("villager", count, locale)} ${en ? "on" : "en"} ${sourceName(assign, locale)}`;
  }

  const name = what === null ? "?" : entityName(what, count, locale);
  const action = ACTIONS[locale][typeof step.do === "string" && Object.hasOwn(ACTIONS[locale], step.do) ? step.do : "move"];
  let line = step.do === "research" ? `${ACTIONS[locale].research} ${name}` : `${action} ${count} ${name}`;
  const builders = integer(step.builders);
  if (step.do === "build" && builders !== null && builders > 1) line += en ? ` (${builders} builders)` : ` (${builders} constructores)`;
  if (assign) line += ` → ${sourceName(assign, locale)}`;
  const atPop = integer(step.at_pop);
  if (atPop !== null) line += en ? ` at ${atPop} pop` : ` a ${atPop} de población`;
  return line;
}
