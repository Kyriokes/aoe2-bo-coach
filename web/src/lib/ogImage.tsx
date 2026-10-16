import { ImageResponse } from "next/og";

/** Tamaño de la vista previa al compartir (el que piden Discord, WhatsApp y X). */
export const OG_SIZE = { width: 1200, height: 630 };

// Misma paleta que globals.css (la imagen se dibuja aparte, sin las variables de CSS).
const colors = {
  background: "#1e1a16",
  panel: "#2b2622",
  line: "#5c5046",
  foreground: "#f4f2ee",
  muted: "#c9c1b6",
  faint: "#a39a8f",
  amber: "#e8c26a",
  green: "#8cd17d",
  blue: "#8fc3e8",
  orange: "#f0a060",
};

// Una línea de tiempo de adorno: barras como las del detalle de una BO (aldeanos, edificios, investigaciones).
const bars = [
  { left: 0, width: 46, color: colors.blue },
  { left: 6, width: 10, color: colors.green },
  { left: 18, width: 12, color: colors.green },
  { left: 30, width: 30, color: colors.blue },
  { left: 44, width: 8, color: colors.amber },
  { left: 54, width: 14, color: colors.green },
  { left: 62, width: 26, color: colors.blue },
  { left: 72, width: 16, color: colors.amber },
];

function truncate(text: string, max: number) {
  return text.length <= max ? text : `${text.slice(0, max - 1).trimEnd()}…`;
}

/**
 * La tarjeta de la vista previa: arriba de qué se trata, el título grande, un renglón de datos, etiquetas chicas
 * (Feudal 8:10, 24 pasos) y una línea de tiempo de adorno abajo.
 */
export function ogCard({ kicker, title, facts, badges = [] }: { kicker: string; title: string; facts: string; badges?: string[] }) {
  return new ImageResponse(
    (
      <div
        style={{
          width: "100%",
          height: "100%",
          display: "flex",
          flexDirection: "column",
          justifyContent: "space-between",
          background: colors.background,
          borderLeft: `16px solid ${colors.amber}`,
          padding: "56px 64px",
          color: colors.foreground,
        }}
      >
        <div style={{ display: "flex", flexDirection: "column", gap: 20 }}>
          <div style={{ display: "flex", fontSize: 28, color: colors.faint }}>{kicker}</div>
          <div style={{ display: "flex", fontSize: 68, lineHeight: 1.1, color: colors.foreground }}>{truncate(title, 60)}</div>
          <div style={{ display: "flex", fontSize: 32, color: colors.muted }}>{truncate(facts, 120)}</div>
          {badges.length > 0 ? (
            <div style={{ display: "flex", gap: 14, marginTop: 8 }}>
              {badges.map((badge) => (
                <div
                  key={badge}
                  style={{
                    display: "flex",
                    fontSize: 28,
                    color: colors.amber,
                    background: colors.panel,
                    border: `2px solid ${colors.line}`,
                    borderRadius: 12,
                    padding: "6px 18px",
                  }}
                >
                  {badge}
                </div>
              ))}
            </div>
          ) : null}
        </div>
        <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
          {bars.map((bar, i) => (
            <div key={i} style={{ display: "flex", height: 10 }}>
              <div style={{ display: "flex", width: `${bar.left}%` }} />
              <div style={{ display: "flex", width: `${bar.width}%`, background: bar.color, borderRadius: 5, opacity: 0.85 }} />
            </div>
          ))}
        </div>
      </div>
    ),
    OG_SIZE,
  );
}
