import type { Metadata } from "next";
import Link from "next/link";
import { notFound, permanentRedirect } from "next/navigation";
import { chanceColor, chanceText, durationLabel, eloLabel, isEven, MAP_NAMES, oneVsOne, pairSlug, parseSlug, type MatchupData } from "@/lib/matchups";
import { getMatchups } from "@/lib/matchupsServer";
import { mapLine, matchupStaticParams, pairCells, pairSummary, rivalsOf, teamOf } from "@/lib/matchupView";
import { MatchupDuration } from "@/components/MatchupDuration";
import { MatchupRivals, type Choice } from "@/components/MatchupRivals";
import { civLabel, SITE_NAME } from "@/lib/share";

/**
 * Todas las páginas se arman al compilar: las de cada civ y las de cada pareja (con las claves en orden alfabético).
 * La pareja al revés y lo que no es ni civ ni pareja los resuelve src/proxy.ts antes de llegar acá (308 y 404 de
 * verdad: con el prerenderizado parcial, una redirección o un notFound armados al pedirlos salían con 200).
 */
export function generateStaticParams() {
  return matchupStaticParams(getMatchups());
}

function mapChoices(data: MatchupData): Choice[] {
  return [{ index: 0, label: "Todos los mapas" }, ...data.mapKeys.map((key, i) => ({ index: i + 1, label: MAP_NAMES[key] ?? key }))];
}

function eloChoices(data: MatchupData): Choice[] {
  return [0, 1, 2, 3, 4, 5].map((band) => ({ index: band, label: band === 0 ? "Todas las franjas" : eloLabel(data, band) }));
}

export async function generateMetadata({ params }: PageProps<"/cruces/[slug]">): Promise<Metadata> {
  const { slug } = await params;
  const parsed = parseSlug(slug);
  if (!parsed || (!("civ" in parsed) && !parsed.canonical)) return { title: "No encontrado", robots: { index: false } };
  const title = "civ" in parsed
    ? `${civLabel(parsed.civ)}: contra qué civs gana`
    : `${civLabel(parsed.a)} contra ${civLabel(parsed.b).toLowerCase()}: estadísticas del cruce`;
  const description = "civ" in parsed
    ? `Cuánto gana ${civLabel(parsed.civ)} contra cada civ de Age of Empires II en 1v1 y cómo le va por equipos, por mapa y Elo, con partidas clasificatorias reales.`
    : `Cuánto gana ${civLabel(parsed.a)} contra ${civLabel(parsed.b)} en Age of Empires II por mapa, Elo y duración de la partida, con partidas clasificatorias reales.`;
  const url = `/cruces/${"civ" in parsed ? parsed.civ : pairSlug(parsed.a, parsed.b)}`;
  return {
    title,
    description,
    alternates: { canonical: url },
    openGraph: { siteName: SITE_NAME, locale: "es_AR", type: "article", title, description, url },
  };
}

export default async function MatchupPage({ params }: PageProps<"/cruces/[slug]">) {
  const { slug } = await params;
  const parsed = parseSlug(slug);
  if (!parsed) notFound();
  if (!("civ" in parsed) && !parsed.canonical) permanentRedirect(`/cruces/${pairSlug(parsed.a, parsed.b)}`);
  const data = getMatchups();
  if (!data) {
    return (
      <div className="flex flex-col gap-4">
        <h1 className="text-2xl font-semibold">Cruces de civs</h1>
        <p className="text-muted">Todavía no hay estadísticas de cruces.</p>
      </div>
    );
  }
  return "civ" in parsed ? <CivMatchups data={data} civ={parsed.civ} /> : <PairMatchup data={data} a={parsed.a} b={parsed.b} />;
}

function Breadcrumb({ civ }: { civ?: string }) {
  return (
    <nav aria-label="Ruta" className="text-sm">
      <Link href="/cruces" className="text-faint hover:text-amber">
        Cruces
      </Link>
      {civ ? (
        <>
          <span className="text-faint"> · </span>
          <Link href={`/cruces/${civ}`} className="text-faint hover:text-amber">
            {civLabel(civ)}
          </Link>
        </>
      ) : null}
    </nav>
  );
}

/** Una civ contra todas en 1v1 (con mapa y Elo para elegir) y cómo le va por equipos en cada mapa. */
function CivMatchups({ data, civ }: { data: MatchupData; civ: string }) {
  const rivals = rivalsOf(data, civ);
  const teamCells = teamOf(data, civ);
  if (rivals.length === 0 && !teamCells) notFound();
  const name = civLabel(civ);
  const maps = mapChoices(data);
  return (
    <article className="flex flex-col gap-8">
      <header className="flex flex-col gap-2">
        <Breadcrumb />
        <h1 className="text-2xl font-semibold">{name}: cómo le va contra cada civ</h1>
        <p className="max-w-3xl text-muted">
          La chance de ganar de {name.toLowerCase()} contra cada rival a Elo parejo, en partidas clasificatorias reales, con un
          rango probable y cuántas partidas hay detrás. Tocá un rival para ver el cruce por mapa y por duración.
        </p>
      </header>

      {rivals.length > 0 ? (
        <section aria-labelledby="rivales" className="flex flex-col gap-3">
          <h2 id="rivales" className="text-lg font-semibold">
            En 1v1
          </h2>
          <MatchupRivals
            civName={name}
            rivals={rivals.map((r) => ({ rival: r.rival, name: civLabel(r.rival), slug: pairSlug(civ, r.rival), cells: r.cells }))}
            maps={maps}
            elos={eloChoices(data)}
          />
        </section>
      ) : null}

      {teamCells ? (
        <section aria-labelledby="equipos" className="flex flex-col gap-3">
          <h2 id="equipos" className="text-lg font-semibold">
            Por equipos
          </h2>
          <p className="max-w-3xl text-sm text-muted">
            La chance de un equipo que tiene a {name.toLowerCase()} en lugar de una civ promedio, a Elo parejo. En equipos se
            cuentan apariciones de la civ (cada jugador), no partidas.
          </p>
          <div className="overflow-x-auto rounded-md border border-line">
            <table className="w-full min-w-[26rem] text-sm">
              <thead className="bg-panel-2 text-left text-faint">
                <tr>
                  <th className="px-3 py-2 font-normal">Mapa</th>
                  <th className="px-3 py-2 font-normal">Ganan</th>
                  <th className="px-3 py-2 text-right font-normal">Apariciones</th>
                </tr>
              </thead>
              <tbody>
                {maps.map((m) => {
                  const c = teamCells[m.index * 6];
                  return (
                    <tr key={m.index} className="border-t border-line">
                      <td className="px-3 py-1.5">{m.label}</td>
                      <td className={`px-3 py-1.5 tabular-nums ${chanceColor(c)}`}>
                        {chanceText(c)}
                        {isEven(c) ? <span className="text-faint"> · parejo</span> : null}
                      </td>
                      <td className="px-3 py-1.5 text-right tabular-nums text-muted">{c.n > 0 ? c.n.toLocaleString("es-AR") : "estimado"}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </section>
      ) : null}

      <Method data={data} />
    </article>
  );
}

/** El cruce de dos civs: el general desde los dos lados, por mapa y por duración. */
function PairMatchup({ data, a, b }: { data: MatchupData; a: string; b: string }) {
  if (!oneVsOne(data, a, b, 0, 0, 0)) notFound();
  const [nameA, nameB] = [civLabel(a), civLabel(b)];
  const summary = pairSummary(data, a, b);
  const { general } = summary;
  const estimated = mapLine(general, null);
  return (
    <article className="flex flex-col gap-8">
      <header className="flex flex-col gap-2">
        <Breadcrumb civ={a} />
        <h1 className="text-2xl font-semibold">
          {nameA} contra {nameB.toLowerCase()}
        </h1>
        <p className="text-lg">
          <span className={chanceColor(general)}>
            {nameA} ganan {chanceText(general)}
          </span>
          {isEven(general) ? <span className="text-faint"> (parejo)</span> : null}
          <span className="text-muted">
            ; {nameB.toLowerCase()}, {summary.rivalPercent} %.
          </span>
        </p>
        <p className="text-sm text-faint">
          {general.n > 0 ? `${general.n.toLocaleString("es-AR")} partidas 1v1 entre las dos civs. ` : `${estimated} `}
          <Link href={`/cruces/${b}`} className="hover:text-amber">
            Cómo le va a {nameB.toLowerCase()} contra todas
          </Link>
        </p>
      </header>

      <section aria-labelledby="mapas" className="flex flex-col gap-3">
        <h2 id="mapas" className="text-lg font-semibold">
          Por mapa
        </h2>
        <div className="overflow-x-auto rounded-md border border-line">
          <table className="w-full min-w-[26rem] text-sm">
            <thead className="bg-panel-2 text-left text-faint">
              <tr>
                <th className="px-3 py-2 font-normal">Mapa</th>
                <th className="px-3 py-2 font-normal">{nameA} ganan</th>
                <th className="px-3 py-2 text-right font-normal">Partidas</th>
              </tr>
            </thead>
            <tbody>
              {summary.maps.map((m) => (
                <tr key={m.name} className="border-t border-line">
                  <td className="px-3 py-1.5">{m.name}</td>
                  <td className={`px-3 py-1.5 tabular-nums ${chanceColor(m.cell)}`}>
                    {chanceText(m.cell)}
                    {isEven(m.cell) ? <span className="text-faint"> · parejo</span> : null}
                  </td>
                  <td className="px-3 py-1.5 text-right tabular-nums text-muted">
                    {m.cell.n > 0 ? m.cell.n.toLocaleString("es-AR") : "estimado"}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <section aria-labelledby="duracion" className="flex flex-col gap-3">
        <h2 id="duracion" className="text-lg font-semibold">
          Según cuánto duró la partida
        </h2>
        <p className="max-w-3xl text-sm text-faint">
          Es lo que pasó, no una receta: la duración es un resultado de la partida (las que se definen temprano duran poco).
        </p>
        <MatchupDuration
          cells={pairCells(data, a, b)}
          civName={nameA}
          rivalName={nameB.toLowerCase()}
          maps={mapChoices(data)}
          elos={eloChoices(data)}
          durations={[1, 2, 3, 4, 5].map((band) => durationLabel(data, band))}
        />
      </section>

      <Method data={data} />
    </article>
  );
}

/** "Cómo se calcula", la fuente, las fechas y el parche. */
function Method({ data }: { data: MatchupData }) {
  return (
    <section aria-labelledby="como" className="flex flex-col gap-2 text-sm text-muted">
      <h2 id="como" className="text-lg font-semibold text-foreground">
        Cómo se calcula
      </h2>
      <ul className="flex max-w-3xl list-disc flex-col gap-1 pl-5">
        <li>Partidas clasificatorias de mapa aleatorio del parche actual, sin espejos ni partidas de menos de 5 minutos de juego.</li>
        <li>
          Primero se descuenta la diferencia de Elo de los jugadores, así lo que queda es de las civs: las chances son a Elo
          parejo.
        </li>
        <li>
          Un modelo suma la fuerza de cada civ y de cada cruce por mapa, franja de Elo y duración; lo que tiene pocas partidas
          queda cerca de lo general, así un casillero con 4 partidas no inventa nada.
        </li>
        <li>
          El rango es donde cae el 90 % de lo probable. Es parejo si toca el 50 %, y por duración solo se dice algo si la franja
          más corta y la más larga no se tocan.
        </li>
        <li>La duración es en minutos del reloj del juego.</li>
      </ul>
      <p className="text-faint">
        Fuente:{" "}
        {data.source.url ? (
          <a href={data.source.url} className="hover:text-amber" rel="noopener noreferrer" target="_blank">
            {data.source.name ?? data.source.url}
          </a>
        ) : (
          (data.source.name ?? "partidas clasificatorias")
        )}
        {data.source.from && data.source.to ? `, partidas del ${data.source.from} al ${data.source.to}` : ""}
        {data.patch.label ? `, parche ${data.patch.label}` : ""}
        {data.patch.previousMixed ? " (con partidas del parche anterior)" : ""}. {data.matches.oneVsOne.toLocaleString("es-AR")} partidas 1v1 y{" "}
        {data.matches.team.toLocaleString("es-AR")} por equipos.
      </p>
    </section>
  );
}
