"use client";

import { createContext, useContext, useMemo, type ReactNode } from "react";
import { getDictionary } from "./index";
import { DEFAULT_LOCALE, localePath, type Locale } from "./locales";

const LocaleContext = createContext<Locale>(DEFAULT_LOCALE);

/** Lo pone el layout: los componentes de cliente leen el idioma con useI18n. */
export function I18nProvider({ locale, children }: { locale: Locale; children: ReactNode }) {
  return <LocaleContext.Provider value={locale}>{children}</LocaleContext.Provider>;
}

/** El idioma, sus textos y `href` para escribir una ruta interna en ese idioma. */
export function useI18n() {
  const locale = useContext(LocaleContext);
  return useMemo(() => ({ locale, t: getDictionary(locale), href: (path: string) => localePath(locale, path) }), [locale]);
}
