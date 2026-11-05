"use client";

import Link from "@/i18n/Link";
import { useState, useTransition } from "react";
import { setSubscription } from "@/app/actions";
import { useI18n } from "@/i18n/client";
import { safeAction } from "@/lib/safeAction";

/**
 * Sumar una BO pública de otro a las que usa la app ("Usar en el coach"). Las propias ya están siempre; sin sesión,
 * lleva a iniciarla.
 */
export function SubscribeButton(props: { buildOrderId: string; code: string; subscribed: boolean; loggedIn: boolean; own: boolean }) {
  const { locale, t: dict, href } = useI18n();
  const t = dict.actions;
  const [pending, startTransition] = useTransition();
  // Estado propio: la acción no refresca la página (las suscripciones no salen en nada cacheado).
  const [subscribed, setSubscribed] = useState(props.subscribed);

  if (props.own) return <span className="text-sm text-faint">{t.subscribeOwn}</span>;
  if (!props.loggedIn) {
    return (
      <Link href={`/login?volver=${href(`/bo/${props.code}`)}`} className="button" title={t.subscribeLogin}>
        {t.subscribe}
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
          const result = await safeAction(() => setSubscription(props.buildOrderId, next, locale), t.failed);
          if (!result.ok) {
            setSubscribed(!next);
            alert(result.error);
          }
        })
      }
      className={subscribed ? "button-primary" : "button"}
      title={subscribed ? t.subscribeRemoveTitle : t.subscribeAddTitle}
      aria-pressed={subscribed}
    >
      {subscribed ? t.subscribed : t.subscribe}
    </button>
  );
}
