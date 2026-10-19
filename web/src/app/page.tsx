import Image from "next/image";
import Link from "next/link";
import { ExampleDemo } from "@/components/ExampleDemo";
import { DOWNLOAD_URL, REPO_URL } from "@/lib/site";

const steps = [
  {
    title: "Lee la pantalla",
    text: "Dos veces por segundo mira el HUD del juego: reloj, recursos, aldeanos en cada recurso, población, la cola de producción y los avisos del juego (\"House Built\", \"Villager Created\").",
  },
  {
    title: "Sigue tu build order",
    text: "Sabe qué paso hiciste, cuál toca ahora y qué quedó atrasado. Si hacés algo antes de tiempo o te demorás, se acomoda en vez de trabarse.",
  },
  {
    title: "Te dice qué hacer",
    text: "Cuántos aldeanos poner en cada recurso ahora y al terminar el paso, cuánto falta para el próximo clic de edad y avisos junto al mouse: sin casas, centro urbano parado, aldeanos inactivos.",
  },
];

const features = [
  "Overlay liviano arriba del juego que no toma los clics (unos 60 MB de RAM y menos del 2 % de CPU).",
  "Editor de build orders con vista previa simulada: ves a qué minuto llegás a Feudal antes de jugar.",
  "Importa las guías de buildorderguide.com y las pasa al formato de la app.",
  "Biblioteca en la web para buscar, votar y compartir build orders, que se abren directo en la app.",
  "Grabar una partida y repetirla para revisar qué vio el coach en cada momento.",
];

const stack = [
  ["App de escritorio", "C# .NET 10 y WPF, con el OCR de Windows y plantillas propias para los números del HUD."],
  ["Motor", "Lógica pura en C#, sin dependencias de Windows: planificador, seguimiento de la BO y economía, con más de 300 tests."],
  ["Web", "Next.js en Vercel y Supabase (Postgres con permisos por fila, cuentas con Discord y Google)."],
  ["Un solo motor", "La web simula las build orders con el mismo motor de la app, compilado a WebAssembly."],
];

export default function HomePage() {
  return (
    <div className="flex flex-col gap-16 py-4">
      <section className="grid items-center gap-8 md:grid-cols-[1.1fr_1fr]">
        <div className="flex flex-col gap-4">
          <p className="text-sm font-medium uppercase tracking-wide text-amber">Age of Empires II: Definitive Edition</p>
          <h1 className="text-3xl font-semibold leading-tight sm:text-4xl">Tu build order, paso a paso, arriba del juego.</h1>
          <p className="max-w-xl text-muted">
            BO Coach es un overlay que lee la pantalla, sigue la build order que elegiste, se adapta cuando te desviás y te
            dice dónde poner a cada aldeano. Para practicar aperturas sin tener una guía abierta en otra pantalla.
          </p>
          <div className="flex flex-wrap gap-3">
            <a href={DOWNLOAD_URL} className="button-primary px-4 py-2 text-base no-underline">
              Descargar para Windows
            </a>
            <Link href="/biblioteca" className="button px-4 py-2 text-base no-underline">
              Ver la biblioteca
            </Link>
          </div>
          <p className="text-xs text-faint">Beta gratis y de código abierto · Windows 10 u 11 · probado en 1920×1080</p>
        </div>
        <figure className="flex flex-col items-center gap-2">
          <Image
            src="/capturas/overlay.png"
            alt="El overlay durante una partida: el paso actual (mover 3 aldeanos de ovejas a madera), lo que está en curso (telar y Edad Feudal) y cómo quedan los aldeanos al terminar el paso."
            width={380}
            height={184}
            priority
            className="w-full max-w-md rounded-lg border border-line shadow-lg"
          />
          <figcaption className="text-xs text-faint">El overlay a los 8:20 de una partida de scouts.</figcaption>
        </figure>
      </section>

      <section aria-labelledby="como-funciona" className="flex flex-col gap-4">
        <h2 id="como-funciona" className="text-xl font-semibold">
          Cómo funciona
        </h2>
        <ol className="grid gap-3 md:grid-cols-3">
          {steps.map((step, i) => (
            <li key={step.title} className="rounded-lg border border-line bg-panel p-4">
              <p className="text-sm font-semibold text-amber">
                {i + 1}. {step.title}
              </p>
              <p className="mt-2 text-sm text-muted">{step.text}</p>
            </li>
          ))}
        </ol>
        <p className="rounded-lg border border-line bg-panel-2 p-4 text-sm text-muted">
          <strong className="text-foreground">Sin trampas:</strong> solo mira la pantalla, como un amigo sentado al lado.
          No lee la memoria del juego, no le inyecta código y no manda teclas ni clics.
        </p>
      </section>

      <section aria-labelledby="probalo" className="flex flex-col gap-4">
        <div>
          <h2 id="probalo" className="text-xl font-semibold">
            Probalo acá mismo
          </h2>
          <p className="mt-1 max-w-2xl text-sm text-muted">
            Las build orders que trae la app, simuladas en tu navegador con el mismo motor que corre en la app: cada paso,
            a qué minuto llega y dónde se traba.
          </p>
        </div>
        <ExampleDemo />
      </section>

      <section aria-labelledby="funciones" className="flex flex-col gap-4">
        <h2 id="funciones" className="text-xl font-semibold">
          Qué más trae
        </h2>
        <ul className="grid gap-2 sm:grid-cols-2">
          {features.map((feature) => (
            <li key={feature} className="rounded-lg border border-line bg-panel p-3 text-sm text-muted">
              {feature}
            </li>
          ))}
        </ul>
      </section>

      <section id="descargar" aria-labelledby="descargar-titulo" className="flex scroll-mt-6 flex-col gap-4">
        <h2 id="descargar-titulo" className="text-xl font-semibold">
          Descargar y empezar
        </h2>
        <ol className="flex max-w-2xl list-decimal flex-col gap-2 pl-5 text-sm text-muted">
          <li>
            Bajá la última versión desde{" "}
            <a href={DOWNLOAD_URL} className="text-amber hover:underline">
              GitHub
            </a>{" "}
            y descomprimila donde quieras. No hace falta instalar nada: trae .NET adentro.
          </li>
          <li>
            Abrí <code className="font-mono text-foreground">BoCoach.exe</code>. Como no está firmado, Windows puede avisar
            que es una aplicación no reconocida: tocá <em>Más información</em> y <em>Ejecutar de todas formas</em>.
          </li>
          <li>
            Con el juego abierto, tocá <em>Calibrar pantalla…</em> (o Ctrl+Alt+C) y ubicá los recuadros sobre el HUD.
          </li>
          <li>
            Elegí una build order de la biblioteca y tocá <em>▶ Empezar coach</em>. El overlay aparece arriba a la derecha
            del juego.
          </li>
        </ol>
        <div>
          <a href={DOWNLOAD_URL} className="button-primary px-4 py-2 text-base no-underline">
            Descargar para Windows
          </a>
        </div>
      </section>

      <section aria-labelledby="como-esta-hecho" className="flex flex-col gap-4">
        <h2 id="como-esta-hecho" className="text-xl font-semibold">
          Cómo está hecho
        </h2>
        <dl className="grid gap-3 sm:grid-cols-2">
          {stack.map(([title, text]) => (
            <div key={title} className="rounded-lg border border-line bg-panel p-4">
              <dt className="text-sm font-semibold text-foreground">{title}</dt>
              <dd className="mt-1 text-sm text-muted">{text}</dd>
            </div>
          ))}
        </dl>
        <p className="text-sm text-muted">
          El código está en{" "}
          <a href={REPO_URL} className="text-amber hover:underline">
            GitHub
          </a>{" "}
          con licencia MIT.
        </p>
      </section>
    </div>
  );
}
