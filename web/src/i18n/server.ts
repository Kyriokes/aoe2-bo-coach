import { locale } from "next/root-params";
import { getDictionary } from "./index";
import { asLocale, type Locale } from "./locales";

/** El idioma de la página, para cualquier componente de servidor (es el segmento [locale] de la URL). */
export async function currentLocale(): Promise<Locale> {
  return asLocale(await locale());
}

/** El idioma de la página y sus textos. */
export async function i18n() {
  const current = await currentLocale();
  return { locale: current, t: getDictionary(current) };
}
