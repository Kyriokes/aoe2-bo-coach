# App remodelada: el tablero

Fecha: 2026-11-02. Pedido del usuario (hoja de ruta, ítem 15, punto 4): "remodelar la app para que sea estética y
amigable para el jugador casual". Eligió la maqueta A entre tres: `2026-11-02-app-remodelada-maqueta.png`.

## Qué tiene que lograr

Al abrir la app se entiende en diez segundos qué hacer: conectar la cuenta, elegir la BO o dejar que elija, jugar y
ver el informe. Lo demás queda fuera de la vista principal. No se pierde ninguna función. El overlay no se toca.

## La ventana principal

De arriba a abajo:

1. **Cabecera:** nombre, versión (los 5 clics del modo desarrollador siguen ahí) y Apoyar, Atajos y Ajustes. Con el
   modo desarrollador o una grabación en curso, también el botón de grabar.
2. **Cuadro de estado:** un título grande, una explicación y la acción principal, con una barra del color del estado.
3. **Los cuatro pasos:** Cuenta, Build order, Jugar e Informe, cada uno con lo que sabe y su enlace.
4. **Build order:** Automática o Elegir de mi lista, la lista, Buscar más, Pegar código y Actualizar. Tocar una BO de
   la lista pasa a "Elegir".
5. **En la partida** (solo con el coach andando): línea, Atrás y Saltear. Lo de diagnóstico sigue solo con el modo
   desarrollador.
6. **Barra de estado:** el último mensaje.

## Los estados del cuadro

Se calculan en un solo lugar y sin ventana (`Coaching/HomeScreen.Build`), así se prueban con tests:

| Estado | Cuándo | Título | Acción principal | Otra |
|---|---|---|---|---|
| En partida | el coach ve el reloj | En partida · 8:12 · francos | Ya está ▶ | Detener coach |
| Terminada | terminó una partida y no empezó otra | Partida terminada · 25:30 | Ver el informe | Listo |
| Sin cuenta | sin token | Conectá tu cuenta de la web | Conectar cuenta | Empezar o detener |
| Esperando | lo demás | según el coach y el juego (abajo) | Empezar ahora, si no corre | Detener, si corre |

"Esperando" se dice de cuatro maneras: "Esperando la partida" (el coach corre), "Esperando el juego" (arranca solo y el
juego está cerrado), "Coach detenido" (lo detuvo el jugador o no arranca solo) y, en modo manual sin BO elegida, la
explicación pide elegir una. Sin cuenta se puede igual empezar el coach, sin BO, con los recordatorios.

## Ajustes

Ventana aparte, no modal, una sola a la vez, con pestañas:

- **Partida:** tu nombre, Elo y Buscar, civ, máximo de aldeanos, recordatorios en el mouse, coach automático. Acá va el
  interruptor "Compartir mis partidas" que hace la sesión del registro de partida (lugar marcado en el XAML, y otro en
  Primeros pasos).
- **Juego y HUD:** idioma del juego, instalar el mod, mod mínimo, Primeros pasos, y cómo están la calibración y el juego.
- **Avanzado:** cuenta de la web (dirección y token) y carpeta de los informes.
- **Desarrollador** (solo con el modo prendido): calibrar a mano, grabar, grabaciones, grabar cada partida sola y
  captura de diagnóstico.

## Las otras ventanas

- **Primeros pasos:** misma estética, cinco pasos numerados. La cuenta pasa a ser el primero: no necesita el juego.
- **Informe de la partida:** XAML con el tema, el título en dorado y los renglones en una tarjeta.
- **Atajos, Cuenta de la web, elegir Elo, pedir un texto y los mensajes:** toman el tema. Los mensajes dejan de ser el
  `MessageBox` de Windows (claro) y usan `AppDialog`.

## Estética

Oscura y a tono con el juego, los mismos colores del overlay y del informe: fondo marrón casi negro, dorado para lo
importante, verde, rojo y azul para los estados. Una sola acción principal en dorado. Segoe UI. Todo en
`Themes/Dark.xaml` (colores, botones, campos, listas, pestañas, barras de desplazamiento, tooltips) y la barra de título
de Windows oscura (`Themes/DarkTitleBar`).

## Textos

Todos los de la app están en `Texts.cs`, en castellano: los toma el XAML con `x:Static` y los view models con métodos
para los que llevan datos. Para traducir, se cambia ese archivo. Quedan afuera los que arma `BoCoach.Core` (pasos,
overlay, informe), que comparte la web, y la calibración a mano, que es de desarrollador.

## Pruebas

Tests de `HomeScreen.Build` por estado y de `HomeViewModel` con los view models reales. Capturas sin el juego:
`--screenshot salida.png [--state sin-cuenta|esperando|en-partida|terminada|elegir] [--settings pestaña] [--report]`, y
`--preview [--no-activate]` abre la ventana de verdad con ajustes temporales, para probar menús y pestañas.
