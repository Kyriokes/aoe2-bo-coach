"use client";

import { useState, useSyncExternalStore } from "react";
import { useI18n } from "@/i18n/client";
import { LOCALE_COOKIE, type Locale } from "@/i18n/locales";

const noSubscribe = () => () => {};

/** Si el visitante no eligió idioma y su navegador prefiere el otro. Solo se sabe en el navegador. */
function suggestedLocale(current: Locale): Locale | null {
  if (document.cookie.split("; ").some((c) => c.startsWith(`${LOCALE_COOKIE}=`))) return null;
  const preferred: Locale = (navigator.language ?? "").toLowerCase().startsWith("es") ? "es" : "en";
  return preferred === current ? null : preferred;
}

/**
 * Un aviso chico para quien cayó en el idioma que no es el suyo (por un enlace o un buscador). No redirige a nadie:
 * los buscadores tienen que poder ver cada idioma en su URL, y el HTML del servidor no cambia.
 */
export function LanguageHint() {
  const { locale, t } = useI18n();
  const [dismissed, setDismissed] = useState(false);
  const other = useSyncExternalStore(
    noSubscribe,
    () => suggestedLocale(locale),
    () => null,
  );
  if (!other || dismissed) return null;
  const stay = () => {
    document.cookie = `${LOCALE_COOKIE}=${locale}; path=/; max-age=31536000; samesite=lax`;
    setDismissed(true);
  };
  return (
    <p lang={other} className="border-b border-line bg-panel-2 px-4 py-1.5 text-center text-xs text-muted">
      {t.layout.hint}{" "}
      <a
        href={`/idioma/${other}`}
        rel="nofollow"
        className="text-amber hover:underline"
        onClick={(e) => {
          const { pathname, search, hash } = window.location;
          e.currentTarget.href = `/idioma/${other}?volver=${encodeURIComponent(pathname + search + hash)}`;
        }}
      >
        {t.layout.hintSwitch}
      </a>
      {" · "}
      <button type="button" className="hover:underline" onClick={stay}>
        {t.layout.hintDismiss}
      </button>
    </p>
  );
}
