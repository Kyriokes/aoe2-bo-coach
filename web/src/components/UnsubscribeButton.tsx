"use client";

import { useRouter } from "next/navigation";
import { useTransition } from "react";
import { setSubscription } from "@/app/actions";
import { useI18n } from "@/i18n/client";
import { safeAction } from "@/lib/safeAction";

/** "Quitar" una suscripción desde Mi cuenta. */
export function UnsubscribeButton({ buildOrderId }: { buildOrderId: string }) {
  const { locale, t } = useI18n();
  const router = useRouter();
  const [pending, startTransition] = useTransition();

  return (
    <button
      type="button"
      className="button py-0.5 text-xs"
      disabled={pending}
      onClick={() =>
        startTransition(async () => {
          const result = await safeAction(() => setSubscription(buildOrderId, false, locale), t.actions.failed);
          if (!result.ok) alert(result.error);
          router.refresh();
        })
      }
    >
      {t.account.remove}
    </button>
  );
}
