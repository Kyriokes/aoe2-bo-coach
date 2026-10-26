import Link from "next/link";
import { Suspense } from "react";
import { UserMenu } from "./UserMenu";

export function Header() {
  return (
    <header className="border-b border-line bg-panel">
      <div className="mx-auto flex w-full max-w-5xl flex-wrap items-center gap-x-6 gap-y-2 px-4 py-3">
        <Link href="/" className="text-base font-semibold tracking-tight text-foreground no-underline">
          AoE2 BO Coach
        </Link>
        {/* En el celular el menú va en su propio renglón, debajo del nombre y del botón de la cuenta. */}
        <nav aria-label="Principal" className="order-last flex basis-full flex-wrap gap-x-4 gap-y-1 text-sm text-muted sm:order-none sm:basis-auto">
          <Link href="/biblioteca" className="hover:text-amber">
            Biblioteca
          </Link>
          <Link href="/cruces" className="hover:text-amber">
            Cruces
          </Link>
          <Link href="/subir" className="hover:text-amber">
            Armar una BO
          </Link>
          <Link href="/#descargar" className="hover:text-amber">
            Descargar
          </Link>
          <Link href="/#apoyar" className="hover:text-amber">
            Apoyar
          </Link>
        </nav>
        <div className="ml-auto">
          <Suspense fallback={<span className="text-sm text-faint">…</span>}>
            <UserMenu />
          </Suspense>
        </div>
      </div>
    </header>
  );
}
