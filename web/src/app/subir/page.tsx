import type { Metadata } from "next";
import Link from "next/link";
import { Suspense } from "react";
import { Uploader } from "@/components/Uploader";
import { getBuildOrder } from "@/lib/data";
import { getCurrentUser } from "@/lib/session";

export const metadata: Metadata = {
  title: "Subir una build order",
  description: "Publicá tu build order de Age of Empires II: se revisa y se simula con el motor de BO Coach antes de subirla.",
  alternates: { canonical: "/subir" },
};

export default function UploadPage({ searchParams }: PageProps<"/subir">) {
  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-2xl font-semibold">Subir una build order</h1>
      <p className="max-w-2xl text-sm text-muted">
        Exportala desde el editor de BO Coach (o pegá el JSON). Antes de publicarla se revisa y se simula con el mismo
        motor que usa la app, así ves la línea de tiempo y si algo no cierra.
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
  const editing = editar ? await getBuildOrder(editar) : null;
  const ownEdit = editing && user && editing.owner_id === user.id ? editing : null;

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
      <Uploader loggedIn={user !== null} editCode={ownEdit?.code} initialJson={ownEdit ? JSON.stringify(ownEdit.body, null, 2) : ""} />
    </>
  );
}
