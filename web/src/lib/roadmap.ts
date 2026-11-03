import en from "@/content/roadmap.en.json";
import es from "@/content/roadmap.es.json";
import type { Locale } from "@/i18n/locales";

/**
 * El roadmap público, un archivo por idioma en src/content (el texto se edita ahí, sin tocar componentes; un test
 * controla que cada archivo tenga la forma y las mismas secciones que el castellano).
 */
export interface RoadmapItem {
  title: string;
  text: string;
}

export interface RoadmapSection {
  id: string;
  title: string;
  intro?: string;
  items: RoadmapItem[];
}

export interface RoadmapContent {
  title: string;
  description: string;
  /** AAAA-MM-DD. */
  updated: string;
  intro: string[];
  sections: RoadmapSection[];
  closing: string;
}

const CONTENT: Readonly<Record<Locale, RoadmapContent>> = { es, en };

export function roadmapContent(locale: Locale): RoadmapContent {
  return CONTENT[locale];
}
