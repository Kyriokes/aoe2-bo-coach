import type { Metadata } from "next";
import { Suspense } from "react";
import { LoginButtons } from "@/components/LoginButtons";

export const metadata: Metadata = { title: "Iniciar sesión" };

export default function LoginPage({ searchParams }: PageProps<"/login">) {
  return (
    <div className="mx-auto flex max-w-sm flex-col gap-4 py-8">
      <h1 className="text-2xl font-semibold">Iniciar sesión</h1>
      <p className="text-sm text-muted">Con tu cuenta podés publicar build orders, votar y conectar la app de escritorio.</p>
      <Suspense fallback={null}>
        <Buttons searchParams={searchParams} />
      </Suspense>
    </div>
  );
}

/** Solo rutas internas: evita que el login redirija a otro sitio. */
function safeNext(value: string | string[] | undefined) {
  return typeof value === "string" && value.startsWith("/") && !value.startsWith("//") ? value : "/";
}

async function Buttons({ searchParams }: { searchParams: PageProps<"/login">["searchParams"] }) {
  const { volver, error } = await searchParams;
  return (
    <>
      {error ? <p className="text-sm text-red">No se pudo iniciar sesión. Probá de nuevo.</p> : null}
      <LoginButtons next={safeNext(volver)} />
    </>
  );
}
