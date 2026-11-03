import { Suspense } from "react";
import Link from "@/i18n/Link";
import { i18n } from "@/i18n/server";
import { LanguageSwitcher } from "./LanguageSwitcher";
import { UserMenu } from "./UserMenu";

export async function Header() {
  const { t } = await i18n();
  return (
    <header className="border-b border-line bg-panel">
      <div className="mx-auto flex w-full max-w-5xl flex-wrap items-center gap-x-6 gap-y-2 px-4 py-3">
        <Link href="/" className="text-base font-semibold tracking-tight text-foreground no-underline">
          AoE2 BO Coach
        </Link>
        {/* En el celular el menú va en su propio renglón, debajo del nombre y del botón de la cuenta. */}
        <nav aria-label={t.nav.main} className="order-last flex basis-full flex-wrap gap-x-4 gap-y-1 text-sm text-muted sm:order-none sm:basis-auto">
          <Link href="/biblioteca" className="hover:text-amber">
            {t.nav.library}
          </Link>
          <Link href="/cruces" className="hover:text-amber">
            {t.nav.matchups}
          </Link>
          <Link href="/subir" className="hover:text-amber">
            {t.nav.build}
          </Link>
          <Link href="/roadmap" className="hover:text-amber">
            {t.nav.roadmap}
          </Link>
          <Link href="/#descargar" className="hover:text-amber">
            {t.nav.download}
          </Link>
          <Link href="/#apoyar" className="hover:text-amber">
            {t.nav.support}
          </Link>
        </nav>
        <div className="ml-auto flex items-center gap-3">
          <LanguageSwitcher />
          <Suspense fallback={<span className="text-sm text-faint">…</span>}>
            <UserMenu />
          </Suspense>
        </div>
      </div>
    </header>
  );
}
