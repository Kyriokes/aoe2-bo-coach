import { jsonLdText } from "@/lib/jsonLd";

/** Datos estructurados de la página (ver src/lib/jsonLd.ts). No se ejecuta: la CSP no lo afecta. */
export function JsonLd({ data }: { data: unknown }) {
  return <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: jsonLdText(data) }} />;
}
