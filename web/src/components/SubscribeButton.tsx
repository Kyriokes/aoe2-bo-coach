"use client";

import Link from "next/link";
import { useState, useTransition } from "react";
import { setSubscription } from "@/app/actions";

/**
 * Sumar una BO pública de otro a las que usa la app ("Usar en el coach"). Las propias ya están siempre; sin sesión,
 * lleva a iniciarla.
 */
export function SubscribeButton(props: { buildOrderId: string; code: string; subscribed: boolean; loggedIn: boolean; own: boolean }) {
  const [pending, startTransition] = useTransition();
  // Estado propio: la acción no refresca la página (las suscripciones no salen en nada cacheado).
  const [subscribed, setSubscribed] = useState(props.subscribed);

  if (props.own) return <span className="text-sm text-faint">Tuya: está en tu coach</span>;
  if (!props.loggedIn) {
    return (
      <Link href={`/login?volver=/bo/${props.code}`} className="button" title="Iniciá sesión para tenerla en la app">
        Usar en el coach
      </Link>
    );
  }

  return (
    <button
      type="button"
      disabled={pending}
      onClick={() =>
        startTransition(async () => {
          const next = !subscribed;
          setSubscribed(next);
          const result = await setSubscription(props.buildOrderId, next);
          if (!result.ok) {
            setSubscribed(!next);
            alert(result.error);
          }
        })
      }
      className={subscribed ? "button-primary" : "button"}
      title={subscribed ? "Sacarla de las BO de tu app" : "Sumarla a las BO de tu app"}
      aria-pressed={subscribed}
    >
      {subscribed ? "En tu coach ✓" : "Usar en el coach"}
    </button>
  );
}
