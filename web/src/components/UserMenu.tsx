import Link from "next/link";
import { signOut } from "@/app/actions";
import { getCurrentUser } from "@/lib/session";

/** Lee la sesión: va dentro de <Suspense>. */
export async function UserMenu() {
  const user = await getCurrentUser();
  if (!user) {
    return (
      <Link href="/login" className="button">
        Iniciar sesión
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
        {user.name}
      </Link>
      <form action={signOut}>
        <button type="submit" className="text-faint hover:text-amber">
          Salir
        </button>
      </form>
    </div>
  );
}
