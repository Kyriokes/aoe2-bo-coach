"use client"; // los límites de error son componentes de cliente

import Link from "next/link";

/** Si algo falla al armar una página (por ejemplo, la base no responde): un mensaje y la opción de reintentar. */
export default function Error({ error, retry }: { error: unknown; retry: () => void }) {
  const digest = typeof error === "object" && error !== null && "digest" in error ? String(error.digest) : null;
  return (
    <div role="alert" className="flex max-w-2xl flex-col gap-3">
      <h1 className="text-2xl font-semibold">Algo salió mal</h1>
      <p className="text-sm text-muted">
        No se pudo cargar esta página. Puede ser un problema momentáneo de la base de datos: probá de nuevo en un rato.
      </p>
      {digest ? <p className="font-mono text-xs text-faint">Código del error: {digest}</p> : null}
      <div className="flex gap-2">
        <button type="button" onClick={() => retry()} className="button-primary">
          Probar de nuevo
        </button>
        <Link href="/" className="button no-underline">
          Ir a la biblioteca
        </Link>
      </div>
    </div>
  );
}
