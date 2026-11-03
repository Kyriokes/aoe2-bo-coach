import type { Metadata } from "next";
import { REPO_URL } from "@/lib/site";

export const metadata: Metadata = {
  title: "Privacidad",
  description: "Qué datos guarda AoE2 BO Coach y para qué.",
};

/** Política de privacidad: la pide Google para publicar el inicio de sesión con Google. */
export default function PrivacyPage() {
  return (
    <article className="flex max-w-2xl flex-col gap-4 text-sm text-muted">
      <h1 className="text-2xl font-semibold text-foreground">Privacidad</h1>
      <p>Última actualización: 19 de octubre de 2026.</p>

      <h2 className="mt-2 text-base font-semibold text-foreground">La app de escritorio</h2>
      <p>
        BO Coach lee la pantalla del juego en tu PC y todo lo que lee se queda ahí. No manda capturas ni datos de tus
        partidas a ningún lado. Las grabaciones que hagas se guardan en tu PC, en <code className="font-mono">%LOCALAPPDATA%\BoCoach</code>.
        La app solo se conecta a esta web si vos lo pedís: para bajar una build order o para publicar una con tu token.
      </p>

      <h2 className="mt-2 text-base font-semibold text-foreground">La web</h2>
      <p>Para mirar la biblioteca y probar las build orders no hace falta cuenta y no se guarda nada tuyo.</p>
      <p>Si iniciás sesión con Discord o con Google, se guarda:</p>
      <ul className="list-disc pl-5">
        <li>tu mail y el identificador de tu cuenta, para que puedas volver a entrar;</li>
        <li>tu nombre y tu foto de perfil, que se muestran como autor de las build orders que publiques;</li>
        <li>las build orders que publiques y tus votos;</li>
        <li>los tokens que crees para la app (solo una huella, no el token).</li>
      </ul>
      <p>
        No se usan para publicidad, no se venden ni se comparten con nadie. Se guardan en Supabase (la base de datos) y el
        sitio corre en Vercel. Las cookies que usa el sitio son solo las de la sesión, para que sigas conectado.
      </p>

      <h2 className="mt-2 text-base font-semibold text-foreground">Borrar tus datos</h2>
      <p>
        Cada build order tuya la podés borrar desde su página, y los tokens desde <em>Mi cuenta</em>. Para borrar la cuenta entera, pedilo
        en{" "}
        <a href={`${REPO_URL}/issues`} className="text-amber hover:underline">
          GitHub
        </a>{" "}
        y se borra junto con todo lo tuyo.
      </p>
    </article>
  );
}
