import type { Metadata, Viewport } from "next";
import Link from "next/link";
import { Geist, Geist_Mono } from "next/font/google";
import { Header } from "@/components/Header";
import { SITE_NAME } from "@/lib/share";
import { siteUrl } from "@/lib/site";
import "./globals.css";

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

const description =
  "Overlay para Age of Empires II: Definitive Edition que lee la pantalla, sigue tu build order paso a paso y te dice dónde poner cada aldeano. Con una biblioteca para compartir build orders.";

const title = `${SITE_NAME}: overlay de build orders para Age of Empires II`;

export const metadata: Metadata = {
  metadataBase: new URL(siteUrl()),
  title: { default: title, template: `%s · ${SITE_NAME}` },
  description,
  applicationName: SITE_NAME,
  openGraph: { siteName: SITE_NAME, locale: "es_AR", type: "website", title, description },
  twitter: { card: "summary_large_image", title, description },
  category: "games",
};

export const viewport: Viewport = { themeColor: "#1e1a16", colorScheme: "dark" };

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="es" className={`${geistSans.variable} ${geistMono.variable} h-full antialiased`}>
      <body className="flex min-h-full flex-col font-sans">
        <a href="#contenido" className="skip-link">
          Saltar al contenido
        </a>
        <Header />
        <main id="contenido" tabIndex={-1} className="mx-auto w-full max-w-5xl flex-1 px-4 py-6 focus:outline-none">
          {children}
        </main>
        <footer className="border-t border-line px-4 py-4 text-center text-xs text-faint">
          AoE2 BO Coach es un proyecto de la comunidad, sin relación con Microsoft ni con los estudios del juego. Solo
          lee la pantalla: no toca la memoria del juego ni manda inputs.{" "}
          <Link href="/privacidad" className="hover:text-amber">
            Privacidad
          </Link>
        </footer>
      </body>
    </html>
  );
}
