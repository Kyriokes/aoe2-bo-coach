"use client";

import Link from "next/link";
import { useOptimistic, useTransition } from "react";
import { setVote } from "@/app/actions";

export function VoteButton(props: { buildOrderId: string; code: string; votes: number; voted: boolean; loggedIn: boolean }) {
  const [pending, startTransition] = useTransition();
  const [state, setOptimistic] = useOptimistic({ votes: props.votes, voted: props.voted });

  if (!props.loggedIn) {
    return (
      <Link href="/login" className="button" title="Iniciá sesión para votar">
        ▲ {props.votes}
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
          await setVote(props.buildOrderId, props.code, vote);
        })
      }
      className={state.voted ? "button-primary" : "button"}
      title={state.voted ? "Sacar el voto" : "Votar"}
    >
      ▲ {state.votes}
    </button>
  );
}
