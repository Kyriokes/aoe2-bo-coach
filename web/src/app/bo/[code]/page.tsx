import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { BuildOrderView } from "@/components/BuildOrderView";
import { JsonLd } from "@/components/JsonLd";
import { OpenInApp } from "@/components/OpenInApp";
import { OwnerActions } from "@/components/OwnerActions";
import { VoteButton, Votes } from "@/components/VoteButton";
import { StepList, stepsOf, type ListedStep } from "@/components/StepList";
import { getBuildOrder } from "@/lib/data";
import { getMatchups } from "@/lib/matchupsServer";
import { civsWithData } from "@/lib/matchupView";
import { GAME } from "@/lib/jsonLd";
import { getCurrentUser } from "@/lib/session";
import { byline, civLabel, shareDescription, SITE_NAME } from "@/lib/share";
import { siteUrl } from "@/lib/site";
import { describeStep } from "@/lib/stepText";
import { sessionClient } from "@/lib/supabase/server";

/** D10: si la civ tiene estadísticas reales de cruces (para enlazar a /cruces/<civ>). */
function hasMatchups(civ: string) {
  const data = getMatchups();
  return civ !== "any" && data !== null && civsWithData(data).includes(civ.toLowerCase());
}

/** "Scouts Feudal · build order de Britanos": el título para los buscadores (la página muestra solo el nombre). */
function pageTitle(bo: { name: string; civ: string }) {
  return bo.civ === "any" ? `${bo.name} · build order para cualquier civ` : `${bo.name} · build order de ${civLabel(bo.civ)}`;
}

export async function generateMetadata({ params }: PageProps<"/bo/[code]">): Promise<Metadata> {
  const { code } = await params;
  const bo = await getBuildOrder(code);
  if (!bo) return { title: "No encontrada", robots: { index: false } };
  // Lo que muestran Discord, WhatsApp o los buscadores al compartir el enlace (la imagen sale de opengraph-image).
  const description = shareDescription(bo);
  const url = `/bo/${bo.code}`;
  const title = pageTitle(bo);
  return {
    title,
    description,
    alternates: { canonical: url },
    authors: [{ name: bo.author }],
    openGraph: {
      type: "article",
      siteName: SITE_NAME,
      locale: "es_AR",
      title,
      description,
      url,
      publishedTime: bo.created_at,
      modifiedTime: bo.updated_at,
      authors: [bo.author],
      tags: bo.tags,
    },
    twitter: { card: "summary_large_image", title, description },
  };
}

/** La BO para los buscadores: una guía paso a paso (HowTo) y el camino Inicio › Biblioteca › BO. */
function structuredData(bo: NonNullable<Awaited<ReturnType<typeof getBuildOrder>>>, steps: ListedStep[]) {
  const base = siteUrl();
  const url = `${base}/bo/${bo.code}`;
  return {
    "@context": "https://schema.org",
    "@graph": [
      {
        "@type": "HowTo",
        "@id": `${url}#bo`,
        name: bo.name,
        description: shareDescription(bo, 500),
        url,
        image: `${url}/opengraph-image`,
        inLanguage: "es",
        datePublished: bo.created_at,
        dateModified: bo.updated_at,
        author: { "@type": "Person", name: bo.author },
        about: GAME,
        keywords: [civLabel(bo.civ), ...bo.tags].join(", "),
        step: steps.slice(0, 100).map((step, i) => ({
          "@type": "HowToStep",
          position: i + 1,
          text: typeof step.note === "string" && step.note ? `${describeStep(step)}. ${step.note}` : describeStep(step),
        })),
      },
      {
        "@type": "BreadcrumbList",
        itemListElement: [
          { "@type": "ListItem", position: 1, name: SITE_NAME, item: base },
          { "@type": "ListItem", position: 2, name: "Biblioteca", item: `${base}/biblioteca` },
          { "@type": "ListItem", position: 3, name: bo.name, item: url },
        ],
      },
    ],
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
  const json = JSON.stringify(bo.body);
  const steps = stepsOf(bo.body);

  return (
    <article className="flex flex-col gap-6">
      <JsonLd data={structuredData(bo, steps)} />
      <header className="flex flex-col gap-2">
        <nav aria-label="Ruta" className="text-sm">
          <Link href="/biblioteca" className="text-faint hover:text-amber">
            ← Biblioteca
          </Link>
          {bo.civ !== "any" ? (
            <>
              <span className="text-faint"> · </span>
              <Link href={`/biblioteca?civ=${encodeURIComponent(bo.civ)}`} className="text-faint hover:text-amber">
                Más de {civLabel(bo.civ)}
              </Link>
            </>
          ) : null}
          {hasMatchups(bo.civ) ? (
            <>
              <span className="text-faint"> · </span>
              <Link href={`/cruces/${bo.civ.toLowerCase()}`} className="text-faint hover:text-amber">
                Cómo le va a esta civ
              </Link>
            </>
          ) : null}
        </nav>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h1 className="text-2xl font-semibold">{bo.name}</h1>
            <p className="text-sm text-faint">
              {civLabel(bo.civ)} · por {byline(bo)} · actualizada el{" "}
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
                <Link href={`/biblioteca?tag=${encodeURIComponent(tag)}`} className="rounded bg-panel-2 px-2 py-0.5 text-xs text-muted hover:text-amber">
                  {tag}
                </Link>
              </li>
            ))}
          </ul>
        ) : null}
      </header>

      <OpenInApp code={bo.code} fileName={bo.name} />
      <Suspense fallback={null}>
        <Owner ownerId={bo.owner_id} code={bo.code} />
      </Suspense>
      <section aria-labelledby="pasos" className="flex flex-col gap-3">
        <h2 id="pasos" className="text-lg font-semibold">
          Pasos{steps.length > 0 ? ` (${steps.length})` : ""}
        </h2>
        <BuildOrderView json={json} fallback={<StepList steps={steps} />} />
      </section>
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
