import type { Metadata } from "next";
import Link from "next/link";
import { Suspense } from "react";
import { ConnectAppButton } from "@/components/ConnectAppButton";
import { TokenManager, type TokenRow } from "@/components/TokenManager";
import { UnsubscribeButton } from "@/components/UnsubscribeButton";
import { VisibilityToggle } from "@/components/VisibilityToggle";
import { formatClock, type Visibility } from "@/lib/buildOrder";
import { byline } from "@/lib/share";
import { mySubscriptions } from "@/lib/ownData";
import { getCurrentUser } from "@/lib/session";
import { sessionClient } from "@/lib/supabase/server";

export const metadata: Metadata = { title: "Mi cuenta", robots: { index: false, follow: false } };

export default function AccountPage() {
  return (
    <Suspense fallback={<p className="text-sm text-faint">Cargando tu cuenta…</p>}>
      <Account />
    </Suspense>
  );
}

async function Account() {
  const user = await getCurrentUser();
  if (!user) {
    return (
      <p className="text-sm text-muted">
        <Link href="/login?volver=/cuenta" className="text-amber hover:underline">
          Iniciá sesión
        </Link>{" "}
        para ver tus build orders y conectar la app.
      </p>
    );
  }

  const supabase = await sessionClient();
  const [{ data: mine }, { data: tokens }, subscriptions] = await Promise.all([
    supabase
      .from("build_orders")
      .select("code, name, votes, feudal_click, updated_at, visibility")
      .eq("owner_id", user.id)
      .order("updated_at", { ascending: false }),
    supabase.from("app_tokens").select("id, label, created_at, last_used_at").order("created_at", { ascending: false }),
    mySubscriptions(),
  ]);
  const own = (mine ?? []) as {
    code: string;
    name: string;
    votes: number;
    feudal_click: number | null;
    updated_at: string;
    visibility: Visibility;
  }[];

  return (
    <div className="flex flex-col gap-8">
      <h1 className="text-2xl font-semibold">Hola, {user.name}</h1>

      <section className="flex flex-col gap-3">
        <h2 className="text-lg font-semibold">Tus build orders</h2>
        {own.length === 0 ? (
          <p className="text-sm text-muted">
            Todavía no publicaste ninguna.{" "}
            <Link href="/subir" className="text-amber hover:underline">
              Armá una
            </Link>{" "}
            o publicala desde la app.
          </p>
        ) : (
          <ul className="divide-y divide-line rounded-md border border-line bg-panel">
            {own.map((bo) => (
              <li key={bo.code} className="flex items-center gap-3 px-3 py-2 text-sm">
                <Link href={`/bo/${bo.code}`} className="flex-1 hover:text-amber">
                  {bo.name}
                </Link>
                <span className="text-xs text-faint">{bo.feudal_click !== null ? `Feudal ${formatClock(bo.feudal_click)}` : ""}</span>
                <span className="font-mono text-xs text-amber">{bo.code}</span>
                <span className="text-xs text-faint">▲ {bo.votes}</span>
                <VisibilityToggle code={bo.code} visibility={bo.visibility} />
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className="flex flex-col gap-3">
        <h2 className="text-lg font-semibold">En tu coach</h2>
        <p className="max-w-2xl text-sm text-muted">
          La app usa tus build orders y las de otros que sumes con &quot;Usar en el coach&quot;.
        </p>
        {subscriptions.length === 0 ? (
          <p className="text-sm text-muted">
            Todavía no sumaste BO de otros:{" "}
            <Link href="/biblioteca" className="text-amber hover:underline">
              buscalas en la biblioteca
            </Link>
            .
          </p>
        ) : (
          <ul className="divide-y divide-line rounded-md border border-line bg-panel">
            {subscriptions.map((bo) => (
              <li key={bo.code} className="flex items-center gap-3 px-3 py-2 text-sm">
                <Link href={`/bo/${bo.code}`} className="flex-1 hover:text-amber">
                  {bo.name}
                </Link>
                <span className="text-xs text-faint">por {byline(bo)}</span>
                <UnsubscribeButton buildOrderId={bo.id} />
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className="flex flex-col gap-3">
        <h2 className="text-lg font-semibold">Conectar la app de escritorio</h2>
        <p className="max-w-2xl text-sm text-muted">
          &quot;Conectar la app&quot; abre BO Coach con tu cuenta. También podés crear un token y pegarlo en la app (Cuenta de
          la web). El token se muestra una sola vez; si lo perdés, borralo y creá otro.
        </p>
        <ConnectAppButton />
        <TokenManager tokens={(tokens ?? []) as TokenRow[]} />
      </section>
    </div>
  );
}
