import Link from "next/link";
import { formatClock } from "@/lib/buildOrder";
import type { BuildOrderSummary } from "@/lib/data";
import { civLabel } from "@/lib/share";
import { Votes } from "./VoteButton";

export function BoCard({ bo }: { bo: BuildOrderSummary }) {
  return (
    <article className="rounded-lg border border-line bg-panel p-4">
      <div className="flex items-start justify-between gap-3">
        <h2 className="text-base font-semibold">
          <Link href={`/bo/${bo.code}`} className="hover:text-amber">
            {bo.name}
          </Link>
        </h2>
        <span className="shrink-0 text-sm text-amber" title="Votos">
          <Votes votes={bo.votes} />
        </span>
      </div>
      <p className="mt-1 text-xs text-faint">
        {civLabel(bo.civ)} · por {bo.author}
        {bo.feudal_click !== null ? ` · Feudal ${formatClock(bo.feudal_click)}` : ""}
        {bo.castle_click !== null ? ` · Castillos ${formatClock(bo.castle_click)}` : ""}
      </p>
      {bo.description ? <p className="mt-2 line-clamp-2 text-sm text-muted">{bo.description}</p> : null}
      {bo.tags.length > 0 ? (
        <ul className="mt-3 flex flex-wrap gap-1.5">
          {bo.tags.map((tag) => (
            <li key={tag}>
              <Link href={`/?tag=${encodeURIComponent(tag)}`} className="rounded bg-panel-2 px-2 py-0.5 text-xs text-muted hover:text-amber">
                {tag}
              </Link>
            </li>
          ))}
        </ul>
      ) : null}
    </article>
  );
}
