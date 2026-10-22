/**
 * Apoyo al proyecto (data/donaciones.json): las formas de donar, las metas y los aportes anotados a mano. Puro, sin
 * React: la web lo lee de /coach/donaciones.json (lo copia scripts/build-coach.mjs) y suma el progreso de cada meta.
 */

export const SUPPORT_SITES = ["cafecito", "paypal", "kofi", "mercado_pago"] as const;
export type SupportSite = (typeof SUPPORT_SITES)[number];

export interface Goal {
  id: string;
  name: string;
  description?: string;
  target: number;
}

export interface Donation {
  date: string;
  amount: number;
  via?: string;
  from?: string;
  goal?: string;
  note?: string;
}

export interface Support {
  links: Partial<Record<SupportSite, string>>;
  currency: string;
  goals: Goal[];
  donations: Donation[];
}

export interface SupportLink {
  site: SupportSite;
  label: string;
  url: string;
}

export interface GoalTally extends Goal {
  raised: number;
  /** 0 a 100, con tope en 100. */
  percent: number;
  done: boolean;
}

export interface Tally {
  goals: GoalTally[];
  total: number;
  count: number;
  /** Lo que entró de más, con todas las metas cumplidas. */
  surplus: number;
  lastDate: string | null;
}

const SITE_LABEL: Record<SupportSite, string> = { cafecito: "Cafecito", paypal: "PayPal", kofi: "Ko-fi", mercado_pago: "Mercado Pago" };
const USER = /^[\w.-]{1,60}$/;

const text = (value: unknown) => (typeof value === "string" ? value.trim() : "");
const amount = (value: unknown) => (typeof value === "number" && Number.isFinite(value) && value > 0 ? value : 0);

export function emptySupport(): Support {
  return { links: {}, currency: "ARS", goals: [], donations: [] };
}

/** Lee el archivo (tiene comentarios de línea); lo que no sirve se ignora en vez de romper la página. */
export function parseSupport(source: string): Support {
  const json = source.replace(/^\s*\/\/.*$/gm, "");
  let root: Record<string, unknown>;
  try {
    root = json.trim() ? (JSON.parse(json) as Record<string, unknown>) : {};
  } catch {
    return emptySupport();
  }
  const support = emptySupport();
  const links = (typeof root.links === "object" && root.links !== null ? root.links : {}) as Record<string, unknown>;
  for (const site of SUPPORT_SITES) {
    const value = text(links[site]);
    if (value) support.links[site] = value;
  }
  if (text(root.currency)) support.currency = text(root.currency).toUpperCase();
  for (const raw of Array.isArray(root.goals) ? root.goals : []) {
    const g = (typeof raw === "object" && raw !== null ? raw : {}) as Record<string, unknown>;
    if (!text(g.id) || !text(g.name) || amount(g.target) === 0) continue;
    support.goals.push({ id: text(g.id), name: text(g.name), target: amount(g.target), ...(text(g.description) ? { description: text(g.description) } : {}) });
  }
  for (const raw of Array.isArray(root.donations) ? root.donations : []) {
    const d = (typeof raw === "object" && raw !== null ? raw : {}) as Record<string, unknown>;
    if (amount(d.amount) === 0) continue;
    const donation: Donation = { date: text(d.date), amount: amount(d.amount) };
    if (text(d.via)) donation.via = text(d.via);
    if (text(d.from)) donation.from = text(d.from);
    if (text(d.goal)) donation.goal = text(d.goal);
    if (text(d.note)) donation.note = text(d.note);
    support.donations.push(donation);
  }
  return support;
}

/** Las formas de donar que están configuradas, con su dirección; un usuario raro o un enlace que no es https se saltea. */
export function supportLinks(links: Support["links"]): SupportLink[] {
  const result: SupportLink[] = [];
  for (const site of SUPPORT_SITES) {
    const value = links[site];
    if (!value) continue;
    let url: string | null = null;
    if (site === "mercado_pago") url = /^https:\/\/[^\s"'<>]+$/.test(value) ? value : null;
    else if (USER.test(value)) url = site === "cafecito" ? `https://cafecito.app/${value}` : site === "paypal" ? `https://paypal.me/${value}` : `https://ko-fi.com/${value}`;
    if (url) result.push({ site, label: SITE_LABEL[site], url });
  }
  return result;
}

/**
 * Cuánto lleva cada meta: los aportes con "goal" van a esa meta; los demás llenan las metas en orden (lo que sobra
 * pasa a la siguiente, y con todas cumplidas queda como excedente).
 */
export function tally(support: Support): Tally {
  const assigned = new Map<string, number>();
  let pool = 0;
  for (const d of support.donations) {
    if (d.goal && support.goals.some((g) => g.id === d.goal)) assigned.set(d.goal, (assigned.get(d.goal) ?? 0) + d.amount);
    else pool += d.amount;
  }
  const goals: GoalTally[] = support.goals.map((g) => {
    let raised = assigned.get(g.id) ?? 0;
    const needed = Math.max(0, g.target - raised);
    const take = Math.min(pool, needed);
    pool -= take;
    raised += take;
    return { ...g, raised, percent: Math.min(100, Math.round((raised / g.target) * 100)), done: raised >= g.target };
  });
  const dates = support.donations.map((d) => d.date).filter((d) => /^\d{4}-\d{2}-\d{2}$/.test(d)).sort();
  return {
    goals,
    total: support.donations.reduce((sum, d) => sum + d.amount, 0),
    count: support.donations.length,
    surplus: pool,
    lastDate: dates.length > 0 ? dates[dates.length - 1] : null,
  };
}

/** "$ 30.000" en el formato de Argentina (o la moneda que sea). */
export function formatAmount(value: number, currency: string) {
  try {
    return new Intl.NumberFormat("es-AR", { style: "currency", currency, maximumFractionDigits: 0 }).format(value);
  } catch {
    return `${Math.round(value)} ${currency}`;
  }
}
