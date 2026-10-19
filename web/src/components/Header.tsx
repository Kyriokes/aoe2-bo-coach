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
        <nav aria-label="Principal" className="flex gap-4 text-sm text-muted">
          <Link href="/biblioteca" className="hover:text-amber">
            Biblioteca
          </Link>
          <Link href="/subir" className="hover:text-amber">
            Subir una BO
          </Link>
          <Link href="/#descargar" className="hover:text-amber">
            Descargar
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
