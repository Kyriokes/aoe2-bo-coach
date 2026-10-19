import { OG_SIZE, ogCard } from "@/lib/ogImage";

export const alt = "AoE2 BO Coach: overlay que sigue tu build order en Age of Empires II";
export const size = OG_SIZE;
export const contentType = "image/png";

export default function Image() {
  return ogCard({
    kicker: "Age of Empires II: Definitive Edition",
    title: "AoE2 BO Coach",
    facts: "Overlay que sigue tu build order paso a paso y te dice dónde poner cada aldeano",
    badges: ["Gratis", "Código abierto", "Solo lee la pantalla"],
  });
}
