"use client";

import Link from "@/i18n/Link";
import { useRouter } from "next/navigation";
import { useTransition } from "react";
import { deleteBuildOrder } from "@/app/actions";
import { useI18n } from "@/i18n/client";
import { safeAction } from "@/lib/safeAction";

/** Editar o borrar una BO propia (la base igual rechaza tocar las ajenas). */
export function OwnerActions({ code }: { code: string }) {
  const { locale, t, href } = useI18n();
  const router = useRouter();
  const [pending, startTransition] = useTransition();

  function remove() {
    if (!confirm(t.actions.ownerDeleteConfirm)) return;
    startTransition(async () => {
      const result = await safeAction(() => deleteBuildOrder(code, locale), t.actions.failed);
      if (result.ok) router.push(href("/cuenta"));
      else alert(result.error);
    });
  }

  return (
    <section className="flex flex-wrap items-center gap-3 text-sm">
      <span className="text-faint">{t.actions.ownerYours}</span>
      <Link href={`/subir?editar=${code}`} className="button">
        {t.actions.ownerEdit}
      </Link>
      <button type="button" onClick={remove} disabled={pending} className="button">
        {t.actions.ownerDelete}
      </button>
    </section>
  );
}
