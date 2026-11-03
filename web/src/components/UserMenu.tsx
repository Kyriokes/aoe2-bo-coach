import { signOut } from "@/app/actions";
import Link from "@/i18n/Link";
import { i18n } from "@/i18n/server";
import { getCurrentUser } from "@/lib/session";

/** Lee la sesión: va dentro de <Suspense>. */
export async function UserMenu() {
  const [user, { locale, t }] = await Promise.all([getCurrentUser(), i18n()]);
  if (!user) {
    return (
      <Link href="/login" className="button">
        {t.user.login}
      </Link>
    );
  }
  return (
    <div className="flex items-center gap-3 text-sm">
      <Link href="/cuenta" className="flex items-center gap-2 text-foreground hover:text-amber">
        {user.avatarUrl ? (
          // eslint-disable-next-line @next/next/no-img-element -- avatares de Discord y Google, sin optimizar
          <img src={user.avatarUrl} alt="" width={24} height={24} className="rounded-full" />
        ) : null}
        {user.name ?? t.user.fallbackName}
      </Link>
      <form action={signOut.bind(null, locale)}>
        <button type="submit" className="text-faint hover:text-amber">
          {t.user.logout}
        </button>
      </form>
    </div>
  );
}
