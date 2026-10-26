import type { Metadata } from "next";
import Link from "next/link";
import { getMatchups } from "@/lib/matchupsServer";
import { civsWithData } from "@/lib/matchupView";
import { civLabel, SITE_NAME } from "@/lib/share";

const TITLE = "Cruces de civs";
const DESCRIPTION =
  "Cuánto gana cada civ de Age of Empires II contra cada rival, por mapa, Elo y duración de la partida, con partidas clasificatorias reales.";

export const metadata: Metadata = {
  title: TITLE,
  description: DESCRIPTION,
  alternates: { canonical: "/cruces" },
  openGraph: { siteName: SITE_NAME, locale: "es_AR", type: "website", title: TITLE, description: DESCRIPTION, url: "/cruces" },
};

/** Elegir dos civs (el formulario va a /cruces/ir, que redirige al cruce) o abrir una civ. */
export default function MatchupsPage() {
  const data = getMatchups();
  if (!data) {
    return (
      <div className="flex flex-col gap-4">
        <h1 className="text-2xl font-semibold">{TITLE}</h1>
        <p className="text-muted">Todavía no hay estadísticas de cruces.</p>
      </div>
    );
  }
  const civs = civsWithData(data).sort((x, y) => civLabel(x).localeCompare(civLabel(y), "es"));
  const options = civs.map((civ) => (
    <option key={civ} value={civ}>
      {civLabel(civ)}
    </option>
  ));
  return (
    <div className="flex flex-col gap-8">
      <header className="flex flex-col gap-2">
        <h1 className="text-2xl font-semibold">{TITLE}</h1>
        <p className="max-w-3xl text-muted">
          Cuánto gana cada civ contra cada rival en partidas clasificatorias reales, por mapa, franja de Elo y duración de la
          partida, con un rango probable y cuántas partidas hay detrás de cada número.
        </p>
        <p className="text-sm text-faint">
          {data.matches.oneVsOne.toLocaleString("es-AR")} partidas 1v1 y {data.matches.team.toLocaleString("es-AR")} por equipos
          {data.source.from && data.source.to ? ` del ${data.source.from} al ${data.source.to}` : ""}
          {data.patch.label ? `, parche ${data.patch.label}` : ""}.
        </p>
      </header>

      <form action="/cruces/ir" method="get" className="flex flex-wrap items-end gap-3 rounded-md border border-line bg-panel p-4">
        <label className="flex flex-col gap-1 text-sm">
          <span className="text-faint">Tu civ</span>
          <select name="a" className="field" defaultValue="franks" required>
            {options}
          </select>
        </label>
        <label className="flex flex-col gap-1 text-sm">
          <span className="text-faint">Contra</span>
          <select name="b" className="field" defaultValue="britons" required>
            {options}
          </select>
        </label>
        <button type="submit" className="button">
          Ver el cruce
        </button>
      </form>

      <section aria-labelledby="civs" className="flex flex-col gap-3">
        <h2 id="civs" className="text-lg font-semibold">
          Cada civ contra todas
        </h2>
        <ul className="grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-5">
          {civs.map((civ) => (
            <li key={civ}>
              <Link href={`/cruces/${civ}`} className="block rounded border border-line bg-panel px-3 py-2 text-sm hover:text-amber">
                {civLabel(civ)}
              </Link>
            </li>
          ))}
        </ul>
      </section>
    </div>
  );
}
