"use client";

import Link from "next/link";
import { useOptimistic, useTransition } from "react";
import { setVote } from "@/app/actions";

/** "▲ 12" en pantalla, "12 votos" para el lector de pantalla. */
export function Votes({ votes }: { votes: number }) {
  return (
    <>
      <span aria-hidden="true">▲</span> {votes}
      <span className="sr-only"> {votes === 1 ? "voto" : "votos"}</span>
    </>
  );
}

export function VoteButton(props: { buildOrderId: string; code: string; votes: number; voted: boolean; loggedIn: boolean }) {
  const [pending, startTransition] = useTransition();
  const [state, setOptimistic] = useOptimistic({ votes: props.votes, voted: props.voted });

  if (!props.loggedIn) {
    return (
      <Link href="/login" className="button" title="Iniciá sesión para votar">
        <Votes votes={props.votes} />
        <span className="sr-only">: iniciá sesión para votar</span>
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
      aria-pressed={state.voted}
    >
      <Votes votes={state.votes} />
      <span className="sr-only">{state.voted ? ": sacar el voto" : ": votar"}</span>
    </button>
  );
}
