import Link from "next/link";

export default function NotFound() {
  return (
    <div className="flex max-w-2xl flex-col gap-3">
      <h1 className="text-2xl font-semibold">No encontramos esa página</h1>
      <p className="text-sm text-muted">
        Puede que la build order se haya borrado o que el enlace esté mal copiado. Los códigos de las BO tienen 6 letras
        y números, como <span className="font-mono text-amber">k7m2qa</span>.
      </p>
      <p>
        <Link href="/biblioteca" className="button no-underline">
          Volver a la biblioteca
        </Link>
      </p>
    </div>
  );
}
