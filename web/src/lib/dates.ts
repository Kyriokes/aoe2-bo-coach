import { LANG_TAG, type Locale } from "@/i18n/locales";

/**
 * El día de una fecha, en UTC: un componente de cliente se dibuja primero en el servidor (en UTC) y después en el navegador
 * (en la zona del usuario), y con otra fecha cerca de la medianoche React no puede hidratar.
 */
export function dayOf(iso: string, locale: Locale = "es") {
  return new Date(iso).toLocaleDateString(LANG_TAG[locale], { timeZone: "UTC" });
}
