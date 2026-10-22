"use client";

import { useEffect, useState } from "react";
import { COACH_BASE } from "@/lib/coach/browser";
import { emptySupport, formatAmount, parseSupport, supportLinks, tally, type Support, type SupportLink } from "@/lib/support";

let loading: Promise<Support> | null = null;

/** data/donaciones.json, copiado a /coach por build-coach (se pide una sola vez). */
function loadSupport(): Promise<Support> {
  loading ??= fetch(`${COACH_BASE}/donaciones.json`)
    .then((r) => (r.ok ? r.text() : ""))
    .then(parseSupport)
    .catch(() => {
      loading = null;
      return emptySupport();
    });
  return loading;
}

function useSupport() {
  const [support, setSupport] = useState<Support | null>(null);
  useEffect(() => {
    let cancelled = false;
    loadSupport().then((s) => {
      if (!cancelled) setSupport(s);
    });
    return () => {
      cancelled = true;
    };
  }, []);
  return support;
}

function LinkButton({ link }: { link: SupportLink }) {
  if (link.site === "cafecito") {
    return (
      <a href={link.url} rel="noopener noreferrer" target="_blank" className="inline-flex">
        {/* eslint-disable-next-line @next/next/no-img-element -- el botón oficial de Cafecito, desde su CDN */}
        <img
          srcSet="https://cdn.cafecito.app/imgs/buttons/button_1.png 1x, https://cdn.cafecito.app/imgs/buttons/button_1_2x.png 2x, https://cdn.cafecito.app/imgs/buttons/button_1_3.75x.png 3.75x"
          src="https://cdn.cafecito.app/imgs/buttons/button_1.png"
          alt="Invitame un café en cafecito.app"
          width={192}
          height={40}
        />
      </a>
    );
  }
  return (
    <a href={link.url} rel="noopener noreferrer" target="_blank" className="button no-underline">
      {link.label}
    </a>
  );
}

/** La sección "Apoyar el proyecto" de la portada: los botones para donar y las metas con su progreso. */
export function SupportSection() {
  const support = useSupport();
  if (!support) return <p className="text-sm text-faint">Cargando…</p>;
  const links = supportLinks(support.links);
  const t = tally(support);
  return (
    <div className="flex flex-col gap-4">
      {links.length > 0 ? (
        <div className="flex flex-wrap items-center gap-3">
          {links.map((link) => (
            <LinkButton key={link.site} link={link} />
          ))}
        </div>
      ) : (
        <p className="text-sm text-faint">Todavía no hay una forma de donar configurada. Pronto.</p>
      )}
      {t.goals.length > 0 ? (
        <ul className="grid gap-3 sm:grid-cols-2" aria-label="Metas">
          {t.goals.map((goal) => (
            <li key={goal.id} className="flex flex-col gap-2 rounded-lg border border-line bg-panel p-4">
              <div className="flex items-baseline justify-between gap-2">
                <p className="text-sm font-semibold text-foreground">
                  {goal.done ? "✓ " : ""}
                  {goal.name}
                </p>
                <p className="shrink-0 text-xs text-faint">
                  {formatAmount(goal.raised, support.currency)} de {formatAmount(goal.target, support.currency)}
                </p>
              </div>
              {goal.description ? <p className="text-xs text-muted">{goal.description}</p> : null}
              <div className="h-2 overflow-hidden rounded bg-panel-2" role="progressbar" aria-valuemin={0} aria-valuemax={100} aria-valuenow={goal.percent} aria-label={`${goal.name}: ${goal.percent} %`}>
                <div className="h-full rounded" style={{ width: `${goal.percent}%`, background: goal.done ? "var(--green)" : "var(--amber)" }} />
              </div>
            </li>
          ))}
        </ul>
      ) : null}
      <p className="text-xs text-faint">
        {t.count === 0
          ? "Todavía no entró ningún aporte."
          : `${t.count} ${t.count === 1 ? "aporte" : "aportes"}, ${formatAmount(t.total, support.currency)} en total${t.surplus > 0 ? ` (${formatAmount(t.surplus, support.currency)} de más, con todas las metas cumplidas)` : ""}${t.lastDate ? `, el último el ${new Date(`${t.lastDate}T12:00:00`).toLocaleDateString("es-AR")}` : ""}.`}{" "}
        Cada aporte se anota a mano en el repo (<code className="font-mono">data/donaciones.json</code>), a la vista de todos.
      </p>
    </div>
  );
}

/** Los enlaces para donar en el pie de todas las páginas. */
export function SupportLinks() {
  const support = useSupport();
  const links = support ? supportLinks(support.links) : [];
  if (links.length === 0) return null;
  return (
    <span>
      {" "}
      ¿Te sirve? Invitame un café en{" "}
      {links.map((link, i) => (
        <span key={link.site}>
          {i > 0 ? (i === links.length - 1 ? " o " : ", ") : ""}
          <a href={link.url} rel="noopener noreferrer" target="_blank" className="hover:text-amber">
            {link.label}
          </a>
        </span>
      ))}
      .
    </span>
  );
}
