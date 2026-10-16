import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { BuildOrderView } from "@/components/BuildOrderView";
import { OpenInApp } from "@/components/OpenInApp";
import { OwnerActions } from "@/components/OwnerActions";
import { VoteButton, Votes } from "@/components/VoteButton";
import { getBuildOrder } from "@/lib/data";
import { getCurrentUser } from "@/lib/session";
import { civLabel, shareDescription, SITE_NAME } from "@/lib/share";
import { sessionClient } from "@/lib/supabase/server";

export async function generateMetadata({ params }: PageProps<"/bo/[code]">): Promise<Metadata> {
  const { code } = await params;
  const bo = await getBuildOrder(code);
  if (!bo) return { title: "No encontrada" };
  // Lo que muestran Discord, WhatsApp o los buscadores al compartir el enlace (la imagen sale de opengraph-image).
  const description = shareDescription(bo);
  const url = `/bo/${bo.code}`;
  return {
    title: bo.name,
    description,
    alternates: { canonical: url },
    openGraph: { type: "article", siteName: SITE_NAME, locale: "es_AR", title: bo.name, description, url },
    twitter: { card: "summary_large_image", title: bo.name, description },
  };
}

export default function BuildOrderPage({ params }: PageProps<"/bo/[code]">) {
  return (
    <Suspense fallback={<p className="text-sm text-faint">Cargando la build order…</p>}>
      <Detail params={params} />
    </Suspense>
  );
}

async function Detail({ params }: { params: PageProps<"/bo/[code]">["params"] }) {
  const { code } = await params;
  const bo = await getBuildOrder(code);
  if (!bo) notFound();
  const json = JSON.stringify(bo.body, null, 2);

  return (
    <article className="flex flex-col gap-6">
      <header className="flex flex-col gap-2">
        <p className="text-sm">
          <Link href="/" className="text-faint hover:text-amber">
            ← Biblioteca
          </Link>
        </p>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h1 className="text-2xl font-semibold">{bo.name}</h1>
            <p className="text-sm text-faint">
              {civLabel(bo.civ)} · por {bo.author} · actualizada el{" "}
              {new Date(bo.updated_at).toLocaleDateString("es-AR")}
            </p>
          </div>
          <Suspense fallback={<span className="button"><Votes votes={bo.votes} /></span>}>
            <Vote buildOrderId={bo.id} code={bo.code} votes={bo.votes} />
          </Suspense>
        </div>
        {bo.description ? <p className="max-w-3xl whitespace-pre-line text-sm text-muted">{bo.description}</p> : null}
        {bo.tags.length > 0 ? (
          <ul className="flex flex-wrap gap-1.5">
            {bo.tags.map((tag) => (
              <li key={tag}>
                <Link href={`/?tag=${encodeURIComponent(tag)}`} className="rounded bg-panel-2 px-2 py-0.5 text-xs text-muted hover:text-amber">
                  {tag}
                </Link>
              </li>
            ))}
          </ul>
        ) : null}
      </header>

      <OpenInApp code={bo.code} json={json} fileName={bo.name} />
      <Suspense fallback={null}>
        <Owner ownerId={bo.owner_id} code={bo.code} />
      </Suspense>
      <BuildOrderView json={json} />
    </article>
  );
}

/** Si el usuario ya votó: lee la sesión, así que va dentro de <Suspense>. */
async function Vote({ buildOrderId, code, votes }: { buildOrderId: string; code: string; votes: number }) {
  const user = await getCurrentUser();
  let voted = false;
  if (user) {
    const supabase = await sessionClient();
    const { data } = await supabase.from("votes").select("build_order_id").eq("build_order_id", buildOrderId).eq("user_id", user.id).maybeSingle();
    voted = data !== null;
  }
  return <VoteButton buildOrderId={buildOrderId} code={code} votes={votes} voted={voted} loggedIn={user !== null} />;
}

async function Owner({ ownerId, code }: { ownerId: string; code: string }) {
  const user = await getCurrentUser();
  return user?.id === ownerId ? <OwnerActions code={code} /> : null;
}
