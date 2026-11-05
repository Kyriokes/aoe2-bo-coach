"use client";

import Link from "@/i18n/Link";
import { useOptimistic, useTransition } from "react";
import { setVote } from "@/app/actions";
import { fill } from "@/i18n";
import { useI18n } from "@/i18n/client";
import { safeAction } from "@/lib/safeAction";

/** "▲ 12" en pantalla, "12 votos" para el lector de pantalla. */
export function Votes({ votes }: { votes: number }) {
  const { t } = useI18n();
  return (
    <>
      <span aria-hidden="true">▲</span> {votes}
      <span className="sr-only"> {fill(votes === 1 ? t.bo.voteOne : t.bo.voteOther, { n: "" }).trim()}</span>
    </>
  );
}

export function VoteButton(props: { buildOrderId: string; code: string; votes: number; voted: boolean; loggedIn: boolean }) {
  const { locale, t: dict, href } = useI18n();
  const t = dict.actions;
  const [pending, startTransition] = useTransition();
  const [state, setOptimistic] = useOptimistic({ votes: props.votes, voted: props.voted });

  if (!props.loggedIn) {
    return (
      <Link href={`/login?volver=${href(`/bo/${props.code}`)}`} className="button" title={t.voteLogin}>
        <Votes votes={props.votes} />
        <span className="sr-only">{t.voteLoginSr}</span>
      </Link>
    );
  }

  return (
    <button
      type="button"
      disabled={pending}
      onClick={() =>
        startTransition(async () => {
          const vote = !state.voted;
          setOptimistic({ voted: vote, votes: state.votes + (vote ? 1 : -1) });
          const result = await safeAction(() => setVote(props.buildOrderId, props.code, vote, locale), t.failed);
          if (!result.ok) alert(result.error);
        })
      }
      className={state.voted ? "button-primary" : "button"}
      title={state.voted ? t.voteRemove : t.voteAdd}
      aria-pressed={state.voted}
    >
      <Votes votes={state.votes} />
      <span className="sr-only">{state.voted ? t.voteRemoveSr : t.voteAddSr}</span>
    </button>
  );
}
