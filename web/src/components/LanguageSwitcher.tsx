"use client";

import { useI18n } from "@/i18n/client";
import { LOCALE_NAME, LOCALES } from "@/i18n/locales";

/**
 * La misma página en otro idioma: pasa por /idioma/<idioma>, que guarda la elección en una cookie y vuelve. La página
 * actual se toma al hacer clic (usePathname daría otra cosa en el servidor, por el rewrite del castellano); sin
 * JavaScript, el enlace lleva a la portada de ese idioma.
 */
export function LanguageSwitcher() {
  const { locale, t } = useI18n();
  return (
    <nav aria-label={t.layout.languageLabel} className="flex items-center gap-1.5 text-xs text-faint">
      {LOCALES.map((other, i) => (
        <span key={other} className="flex items-center gap-1.5">
          {i > 0 ? <span aria-hidden="true">·</span> : null}
          {other === locale ? (
            <span aria-current="true" className="text-foreground">
              {LOCALE_NAME[other]}
            </span>
          ) : (
            <a
              href={`/idioma/${other}`}
              hrefLang={other}
              lang={other}
              rel="nofollow"
              className="hover:text-amber"
              onClick={(e) => {
                const { pathname, search, hash } = window.location;
                e.currentTarget.href = `/idioma/${other}?volver=${encodeURIComponent(pathname + search + hash)}`;
              }}
            >
              {LOCALE_NAME[other]}
            </a>
          )}
        </span>
      ))}
    </nav>
  );
}
