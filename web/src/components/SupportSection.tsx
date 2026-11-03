"use client";

import { useEffect, useState } from "react";
import { fill, rich } from "@/i18n";
import { useI18n } from "@/i18n/client";
import { LANG_TAG, type Locale } from "@/i18n/locales";
import { COACH_BASE } from "@/lib/coach/browser";
import { emptySupport, formatAmount, parseSupport, supportLinks, tally, type Support, type SupportLink } from "@/lib/support";

let loading: Promise<Support> | null = null;

/** data/donaciones.json, copiado a /coach por build-coach (se pide una sola vez). */
function loadSupport(locale: Locale): Promise<Support> {
  loading ??= fetch(`${COACH_BASE}/donaciones.json`)
    .then((r) => (r.ok ? r.text() : ""))
    .then((source) => parseSupport(source, locale))
    .catch(() => {
      loading = null;
      return emptySupport();
    });
  return loading;
}

function useSupport(locale: Locale) {
  const [support, setSupport] = useState<Support | null>(null);
  useEffect(() => {
    let cancelled = false;
    loadSupport(locale).then((s) => {
      if (!cancelled) setSupport(s);
    });
    return () => {
      cancelled = true;
    };
  }, [locale]);
  return support;
}

function LinkButton({ link }: { link: SupportLink }) {
  const { t } = useI18n();
  if (link.site === "cafecito") {
    return (
      <a href={link.url} rel="noopener noreferrer" target="_blank" className="inline-flex">
        {/* eslint-disable-next-line @next/next/no-img-element -- el botón oficial de Cafecito, desde su CDN */}
        <img
          srcSet="https://cdn.cafecito.app/imgs/buttons/button_1.png 1x, https://cdn.cafecito.app/imgs/buttons/button_1_2x.png 2x, https://cdn.cafecito.app/imgs/buttons/button_1_3.75x.png 3.75x"
          src="https://cdn.cafecito.app/imgs/buttons/button_1.png"
          alt={t.support.cafecitoAlt}
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
  const { locale, t: dict } = useI18n();
  const t = dict.support;
  const support = useSupport(locale);
  const money = (value: number) => formatAmount(value, support?.currency ?? "ARS", locale);
  if (!support) return <p className="text-sm text-faint">{t.loading}</p>;
  const links = supportLinks(support.links);
  const counted = tally(support);
  return (
    <div className="flex flex-col gap-4">
      {links.length > 0 ? (
        <div className="flex flex-wrap items-center gap-3">
          {links.map((link) => (
            <LinkButton key={link.site} link={link} />
          ))}
        </div>
      ) : (
        <p className="text-sm text-faint">{t.noLinks}</p>
      )}
      {counted.goals.length > 0 ? (
        <ul className="grid gap-3 sm:grid-cols-2" aria-label={t.goalsLabel}>
          {counted.goals.map((goal) => (
            <li key={goal.id} className="flex flex-col gap-2 rounded-lg border border-line bg-panel p-4">
              <div className="flex items-baseline justify-between gap-2">
                <p className="text-sm font-semibold text-foreground">
                  {goal.done ? "✓ " : ""}
                  {goal.name}
                </p>
                <p className="shrink-0 text-xs text-faint">
                  {fill(t.ofTarget, { raised: money(goal.raised), target: money(goal.target) })}
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
        {counted.count === 0
          ? t.none
          : `${fill(t.total, { count: fill(counted.count === 1 ? t.countOne : t.countOther, { n: counted.count }), total: money(counted.total) })}${counted.surplus > 0 ? fill(t.surplus, { amount: money(counted.surplus) }) : ""}${counted.lastDate ? fill(t.last, { date: new Date(`${counted.lastDate}T12:00:00`).toLocaleDateString(LANG_TAG[locale]) }) : ""}.`}{" "}
        {rich(t.byHand, { code: (text) => <code className="font-mono">{text}</code> })}
      </p>
    </div>
  );
}

/** Los enlaces para donar en el pie de todas las páginas. */
export function SupportLinks() {
  const { locale, t } = useI18n();
  const support = useSupport(locale);
  const links = support ? supportLinks(support.links) : [];
  if (links.length === 0) return null;
  return (
    <span>
      {" "}
      {t.support.footer}{" "}
      {links.map((link, i) => (
        <span key={link.site}>
          {i > 0 ? (i === links.length - 1 ? t.support.or : ", ") : ""}
          <a href={link.url} rel="noopener noreferrer" target="_blank" className="hover:text-amber">
            {link.label}
          </a>
        </span>
      ))}
      .
    </span>
  );
}
