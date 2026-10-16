import { OG_SIZE, ogCard } from "@/lib/ogImage";

export const alt = "AoE2 BO Coach: biblioteca de build orders para Age of Empires II";
export const size = OG_SIZE;
export const contentType = "image/png";

export default function Image() {
  return ogCard({
    kicker: "Age of Empires II: Definitive Edition",
    title: "AoE2 BO Coach",
    facts: "Biblioteca de build orders: mirá la línea de tiempo y seguila en vivo con el overlay",
    badges: ["Gratis", "Código abierto", "Solo lee la pantalla"],
  });
}
