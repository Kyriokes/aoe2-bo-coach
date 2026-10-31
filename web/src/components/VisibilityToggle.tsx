"use client";

import { useState, useTransition } from "react";
import { setVisibility } from "@/app/actions";
import type { Visibility } from "@/lib/buildOrder";

/** Pública o privada, para una BO propia en "Mi cuenta". */
export function VisibilityToggle({ code, visibility }: { code: string; visibility: Visibility }) {
  const [value, setValue] = useState(visibility);
  const [pending, startTransition] = useTransition();

  return (
    <select
      className="field py-0.5 text-xs"
      value={value}
      disabled={pending}
      aria-label="Quién la ve"
      onChange={(e) => {
        const next = e.target.value as Visibility;
        const before = value;
        setValue(next);
        startTransition(async () => {
          const result = await setVisibility(code, next);
          if (!result.ok) {
            setValue(before);
            alert(result.error);
          }
        });
      }}
    >
      <option value="public">Pública</option>
      <option value="private">Privada</option>
    </select>
  );
}
