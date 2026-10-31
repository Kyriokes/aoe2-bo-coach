"use client";

import { useRouter } from "next/navigation";
import { useTransition } from "react";
import { setSubscription } from "@/app/actions";

/** "Quitar" una suscripción desde Mi cuenta. */
export function UnsubscribeButton({ buildOrderId }: { buildOrderId: string }) {
  const router = useRouter();
  const [pending, startTransition] = useTransition();

  return (
    <button
      type="button"
      className="button py-0.5 text-xs"
      disabled={pending}
      onClick={() =>
        startTransition(async () => {
          const result = await setSubscription(buildOrderId, false);
          if (!result.ok) alert(result.error);
          router.refresh();
        })
      }
    >
      Quitar
    </button>
  );
}
