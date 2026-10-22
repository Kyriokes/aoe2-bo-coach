"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useTransition } from "react";
import { deleteBuildOrder } from "@/app/actions";

/** Editar o borrar una BO propia (la base igual rechaza tocar las ajenas). */
export function OwnerActions({ code }: { code: string }) {
  const router = useRouter();
  const [pending, startTransition] = useTransition();

  function remove() {
    if (!confirm("¿Borrar esta build order de la biblioteca? No se puede deshacer.")) return;
    startTransition(async () => {
      const result = await deleteBuildOrder(code);
      if (result.ok) router.push("/cuenta");
      else alert(result.error);
    });
  }

  return (
    <section className="flex flex-wrap items-center gap-3 text-sm">
      <span className="text-faint">Es tuya:</span>
      <Link href={`/subir?editar=${code}`} className="button">
        Editar en la web
      </Link>
      <button type="button" onClick={remove} disabled={pending} className="button">
        Borrar
      </button>
    </section>
  );
}
