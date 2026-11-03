import { Fragment, type ReactNode } from "react";
import { en } from "./en";
import { es, type Dictionary } from "./es";
import type { Locale } from "./locales";

export type { Dictionary };

const DICTIONARIES: Readonly<Record<Locale, Dictionary>> = { es, en };

/** Los textos de un idioma. Los dos diccionarios van en el bundle: son livianos y es la misma función en todos lados. */
export function getDictionary(locale: Locale = "es"): Dictionary {
  return DICTIONARIES[locale];
}

/** Reemplaza los {marcadores} de un texto. Un marcador sin valor queda a la vista: mejor que un hueco silencioso. */
export function fill(template: string, values: Readonly<Record<string, string | number>> = {}): string {
  return template.replace(/\{(\w+)\}/g, (whole, key: string) => (Object.hasOwn(values, key) ? String(values[key]) : whole));
}

/**
 * Un texto con etiquetas: `rich("Bajalo de <link>GitHub</link>.", { link: (text) => <a href="...">{text}</a> })`. Así
 * cada idioma ordena la frase como quiere y el enlace no parte el texto en pedazos. Una etiqueta sin componente se
 * muestra como texto. Las etiquetas no se anidan.
 */
export function rich(template: string, tags: Readonly<Record<string, (text: string) => ReactNode>>): ReactNode {
  const parts = template.split(/(<\w+>.*?<\/\w+>)/g);
  return parts.map((part, i) => {
    const match = /^<(\w+)>(.*?)<\/\1>$/.exec(part);
    if (!match) return <Fragment key={i}>{part}</Fragment>;
    const render = Object.hasOwn(tags, match[1]) ? tags[match[1]] : null;
    return <Fragment key={i}>{render ? render(match[2]) : match[2]}</Fragment>;
  });
}
