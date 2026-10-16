import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import { Header } from "@/components/Header";
import "./globals.css";

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: { default: "AoE2 BO Coach", template: "%s · AoE2 BO Coach" },
  description:
    "Biblioteca de build orders para Age of Empires II: Definitive Edition. Mirá la línea de tiempo de cada BO y abrila en BO Coach para seguirla durante la partida.",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="es" className={`${geistSans.variable} ${geistMono.variable} h-full antialiased`}>
      <body className="flex min-h-full flex-col font-sans">
        <Header />
        <main className="mx-auto w-full max-w-5xl flex-1 px-4 py-6">{children}</main>
        <footer className="border-t border-line px-4 py-4 text-center text-xs text-faint">
          AoE2 BO Coach es un proyecto de la comunidad, sin relación con Microsoft ni con los estudios del juego. Solo
          lee la pantalla: no toca la memoria del juego ni manda inputs.
        </footer>
      </body>
    </html>
  );
}
