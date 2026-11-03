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
      <p>Última actualización: 2 de noviembre de 2026.</p>

      <h2 className="mt-2 text-base font-semibold text-foreground">La app de escritorio</h2>
      <p>
        BO Coach lee la pantalla del juego en tu PC. Nunca manda capturas de pantalla. Las grabaciones y los registros de
        cada partida se guardan en tu PC, en <code className="font-mono">%LOCALAPPDATA%\BoCoach</code>. La app se conecta a esta
        web para bajar tus build orders y, si no lo apagás, para compartir tus partidas.
      </p>

      <h2 className="mt-2 text-base font-semibold text-foreground">Compartir tus partidas</h2>
      <p>
        La opción &quot;Compartir mis partidas para mejorar el coach&quot; viene prendida y se apaga en la ventana de la app o en
        Primeros pasos. Con ella prendida, al terminar cada partida de un minuto o más se sube un registro con:
      </p>
      <ul className="list-disc pl-5">
        <li>los números que el coach leyó del HUD (reloj, recursos, aldeanos, población, cola de producción y puntajes);</li>
        <li>los mensajes del juego que reconoció en el log, y lo que creyó, pronosticó y te mostró;</li>
        <li>tu civ, la del rival de enfrente, el mapa, la build order, la versión de la app y el idioma del juego.</li>
      </ul>
      <p>
        No viaja tu nombre, ni el de los otros jugadores, ni el chat (lo que el log no reconoce como un mensaje del juego se
        tapa antes de guardarlo), ni capturas de pantalla. El registro no está atado a tu cuenta de la web: lo identifica un
        número al azar de esa instalación de la app. Se guarda en Supabase, en un lugar que no es público, y se usa solo
        para encontrar lo que el coach lee o aconseja mal.
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
        Los registros que subió tu PC se borran con &quot;Borrar lo compartido…&quot; en la ventana de la app. Cada build order tuya
        la podés borrar desde su página, y los tokens desde <em>Mi cuenta</em>. Para borrar la cuenta entera, pedilo
        en{" "}
        <a href={`${REPO_URL}/issues`} className="text-amber hover:underline">
          GitHub
        </a>{" "}
        y se borra junto con todo lo tuyo.
      </p>
    </article>
  );
}
