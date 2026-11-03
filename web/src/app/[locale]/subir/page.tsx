import type { Metadata } from "next";
import Link from "next/link";
import { Suspense } from "react";
import { BuildOrderEditor } from "@/components/editor/BuildOrderEditor";
import { getOwnBuildOrder } from "@/lib/ownData";
import { getCurrentUser } from "@/lib/session";

export const metadata: Metadata = {
  title: "Armar una build order",
  description:
    "Armá tu build order de Age of Empires II arrastrando pasos a una línea de tiempo: cada cambio se simula con el motor de BO Coach y se publica en la biblioteca.",
  alternates: { canonical: "/subir" },
};

export default function UploadPage({ searchParams }: PageProps<"/subir">) {
  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-2xl font-semibold">Armar una build order</h1>
      <p className="max-w-2xl text-sm text-muted">
        Arrastrá pasos de la paleta a la línea de tiempo: cada cambio se simula con el mismo motor que usa la app, y
        ninguna caja puede ir a donde el juego no la deja. También podés abrir un JSON del editor de la app o empezar
        de un ejemplo.
      </p>
      <Suspense fallback={<p className="text-sm text-faint">Cargando…</p>}>
        <Upload searchParams={searchParams} />
      </Suspense>
    </div>
  );
}

async function Upload({ searchParams }: { searchParams: PageProps<"/subir">["searchParams"] }) {
  const params = await searchParams;
  const user = await getCurrentUser();
  const editar = typeof params.editar === "string" ? params.editar : undefined;
  // Se lee con la sesión: una privada propia no está en el caché público.
  const ownEdit = editar && user ? await getOwnBuildOrder(editar.toLowerCase()) : null;

  return (
    <>
      {!user ? (
        <p className="rounded-md border border-line bg-panel p-3 text-sm text-muted">
          Podés revisar tu BO sin cuenta. Para publicarla, <Link href="/login?volver=/subir" className="text-amber hover:underline">iniciá sesión</Link>.
        </p>
      ) : null}
      {ownEdit ? (
        <p className="text-sm text-muted">
          Reemplazando <Link href={`/bo/${ownEdit.code}`} className="text-amber hover:underline">{ownEdit.name}</Link>: se mantienen el
          código y los votos.
        </p>
      ) : null}
      <BuildOrderEditor
        loggedIn={user !== null}
        editCode={ownEdit?.code}
        initialJson={ownEdit ? JSON.stringify(ownEdit.body) : ""}
        initialVisibility={ownEdit?.visibility}
      />
    </>
  );
}
