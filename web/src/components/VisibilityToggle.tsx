"use client";

import { useState, useTransition } from "react";
import { setVisibility } from "@/app/actions";
import { useI18n } from "@/i18n/client";
import type { Visibility } from "@/lib/buildOrder";
import { safeAction } from "@/lib/safeAction";

/** Pública o privada, para una BO propia en "Mi cuenta". */
export function VisibilityToggle({ code, visibility }: { code: string; visibility: Visibility }) {
  const { locale, t } = useI18n();
  const [value, setValue] = useState(visibility);
  const [pending, startTransition] = useTransition();

  return (
    <select
      className="field py-0.5 text-xs"
      value={value}
      disabled={pending}
      aria-label={t.account.visibilityLabel}
      onChange={(e) => {
        const next = e.target.value as Visibility;
        const before = value;
        setValue(next);
        startTransition(async () => {
          const result = await safeAction(() => setVisibility(code, next, locale), t.actions.failed);
          if (!result.ok) {
            setValue(before);
            alert(result.error);
          }
        });
      }}
    >
      <option value="public">{t.account.public}</option>
      <option value="private">{t.account.private}</option>
    </select>
  );
}
