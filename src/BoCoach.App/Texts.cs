using static BoCoach.Core.Lang;

namespace BoCoach.App;

/// <summary>
/// Todos los textos de la app, en castellano e inglés y en un solo lugar: el XAML los toma con
/// <c>{x:Static app:Texts.X}</c> y los view models, directo; los que llevan datos son métodos. El idioma es el de
/// <see cref="BoCoach.Core.Lang"/>, que se elige al arrancar (cambiarlo reabre la app, porque el XAML los lee una vez).
/// Los que arma BoCoach.Core (pasos, overlay, informe) están en el Core. Quedan solo en castellano la calibración a mano
/// y el .txt de diagnóstico, que son de desarrollador.
/// </summary>
public static class Texts
{
    // ---- La app ----
    public static string AppName => "AoE2 BO Coach";
    public static string VersionTip => L("Versión · commit · cuándo se compiló", "Version · commit · build time");
    public static string SupportButton => L("Apoyar ☕", "Support ☕");
    public static string SupportTip => L("Invitame un café (Cafecito, PayPal, Ko-fi o Mercado Pago) y mirá las metas del proyecto en la web", "Buy me a coffee (Cafecito, PayPal, Ko-fi or Mercado Pago) and see the project's goals on the website");
    public static string HotkeysButton => L("Atajos", "Hotkeys");
    public static string HotkeysTip => L("Ver y cambiar los atajos de teclado que andan desde el juego", "See and change the keyboard shortcuts that work from inside the game");
    public static string SettingsButton => L("Ajustes ⚙", "Settings ⚙");
    public static string SettingsTip => L("Tu nombre, civ, idioma del juego, el mod del HUD y las demás opciones", "Your name, civ, game language, the HUD mod and the other options");
    public static string DevModeOn => L("Modo desarrollador prendido.", "Developer mode on.");
    public static string DevModeOff => L("Modo desarrollador apagado.", "Developer mode off.");
    public static string Accept => L("Aceptar", "OK");
    public static string Cancel => L("Cancelar", "Cancel");
    public static string Close => L("Cerrar", "Close");
    public static string Yes => L("Sí", "Yes");
    public static string No => "No";
    public static string ConfirmTitle => L("Confirmar", "Confirm");

    public static string HotkeysInUse(string keys) => L($"Otro programa ya usa {keys}; usá los botones o cambialos en Atajos.", $"Another program already uses {keys}; use the buttons or change them in Hotkeys.");
    public static string HotkeysReady(string startStop, string advance) => L(
        $"Atajos listos, también desde el juego: {startStop} empieza el coach y {advance} da por hecho el paso (todos en Atajos).",
        $"Hotkeys ready, also from inside the game: {startStop} starts the coach and {advance} marks the step as done (all of them in Hotkeys).");
    public static string HotkeyMissing(string name) => L($"{name} (sin atajo)", $"{name} (no hotkey)");

    // ---- El idioma de la app ----
    public static string AppLanguageLabel => L("Idioma de la app", "App language");
    public static string AppLanguageTip => L("El de esta ventana, el overlay y el informe. El del juego es otra cosa: se elige abajo.", "The language of this window, the overlay and the report. The game's language is a separate setting, below.");
    public static string RestartForLanguage => L("Para cambiar el idioma la app se vuelve a abrir. ¿La reabro ahora?", "To change the language the app has to reopen. Reopen it now?");

    /// <summary>Una página de la web en el idioma de la app: el castellano va sin prefijo.</summary>
    public static string WebPath(string page) => IsEnglish ? $"en/{page}".TrimEnd('/') : page;

    /// <summary>El nombre de cada idioma, siempre en ese idioma: es como se lo reconoce.</summary>
    public static string LanguageName(string language) => language == English ? "English" : "Español";

    // ---- El cuadro de estado ----
    public static string NoAccountTitle => L("Conectá tu cuenta de la web", "Connect your web account");
    public static string NoAccountDetail => L("Ahí se eligen las build orders. Se abre Mi cuenta en el navegador y «Conectar la app» la trae acá.", "That's where build orders are chosen. My account opens in your browser and \"Connect the app\" brings it here.");
    public static string WaitingGameTitle => L("Esperando el juego", "Waiting for the game");
    public static string WaitingGameDetail => L("Abrí Age of Empires II: el coach arranca solo y se ve encima de la partida.", "Open Age of Empires II: the coach starts on its own and shows on top of the game.");
    public static string WaitingMatchTitle => L("Esperando la partida", "Waiting for a match");
    public static string WaitingMatchGameOpen => L("Empezá una partida: en la pantalla de carga el coach ve tu civ, tu rival y el mapa.", "Start a match: on the loading screen the coach sees your civ, your rival and the map.");
    public static string WaitingMatchGameClosed => L("Abrí Age of Empires II y empezá una partida: el coach se ve encima.", "Open Age of Empires II and start a match: the coach shows on top.");
    public static string StoppedTitle => L("Coach detenido", "Coach stopped");
    public static string StoppedAutoOff => L("El coach automático está apagado: tocá «Empezar ahora» cuando vayas a jugar.", "The automatic coach is off: press \"Start now\" when you're going to play.");
    public static string StoppedByYou => L("Lo detuviste: tocá «Empezar ahora» para seguir. Solo vuelve a arrancar cuando abras el juego de nuevo.", "You stopped it: press \"Start now\" to continue. It only starts on its own again when you reopen the game.");
    public static string NeedsBuildOrder => L("Elegí una BO de la lista de abajo, o pasá a Automática.", "Choose a BO from the list below, or switch to Automatic.");
    public static string InGameFallback => L("El coach está siguiendo la partida.", "The coach is following the match.");
    public static string FinishedFallback => L("Quedó guardado el informe de la partida.", "The match report was saved.");
    public static string StartNow => L("▶ Empezar ahora", "▶ Start now");
    public static string SeeReport => L("Ver el informe", "See the report");
    public static string ReportSeen => L("Listo", "Done");

    public static string InGameTitle(string clock, string? civ) =>
        civ is null ? L($"En partida · {clock}", $"In game · {clock}") : L($"En partida · {clock} · {civ}", $"In game · {clock} · {civ}");

    public static string FinishedTitle(string length) => L($"Partida terminada · {length}", $"Match finished · {length}");

    // ---- Los cuatro pasos ----
    public static string StepAccount => L("Cuenta", "Account");
    public static string StepBuildOrder => "Build order";
    public static string StepPlay => L("Jugar", "Play");
    public static string StepReport => L("Informe", "Report");
    public static string StepAccountNone => L("Sin conectar", "Not connected");
    public static string StepAccountLink => L("Cuenta…", "Account…");
    public static string StepBuildOrderAuto => L("Automática: la sugerida para tu civ, mapa y rival", "Automatic: the one suggested for your civ, map and rival");
    public static string StepBuildOrderMissing => L("Falta elegir una de la lista", "Choose one from the list");
    public static string StepPlayGameClosed => L("Abrí el juego: el coach se ve encima.", "Open the game: the coach shows on top.");
    public static string StepPlayGameOpen => L("Juego abierto: empezá una partida.", "Game open: start a match.");
    public static string StepPlayInGame => L("En partida.", "In game.");
    public static string StepPlayNoName => L("Falta tu nombre en el juego.", "Your in-game name is missing.");
    public static string StepReportNone => L("Se arma solo al terminar una partida.", "It's made on its own when a match ends.");
    public static string StepReportOpen => L("Abrir", "Open");
    public static string StepReportAll => L("Ver todos", "See all");

    public static string StepBuildOrderManual(string name) => L($"A mano: {name}", $"Manual: {name}");
    public static string StepReportLast(DateTime date, string? length) => length is null
        ? L($"Último: {date:d/M HH:mm}", $"Last: {date:d/M HH:mm}")
        : L($"Último: {date:d/M HH:mm} · {length} de partida", $"Last: {date:d/M HH:mm} · {length} match");
    public static string ReportStepsDone(int done, int total) => L($"{done} de {total} pasos", $"{done} of {total} steps");
    public static string ReportIdle(string time) => L($"centro urbano parado {time}", $"town center idle {time}");

    // ---- Build order ----
    public static string BuildOrderSection => "BUILD ORDER";
    public static string ModeAuto => L("Automática: la sugerida para tu civ, mapa y rival en cada partida", "Automatic: the one suggested for your civ, map and rival each match");
    public static string ModeManual => L("Elegir de mi lista", "Choose from my list");
    public static string ConnectAccount => L("Conectar cuenta", "Connect account");
    public static string ConnectAccountTip => L("Abre Mi cuenta en la web: ahí \"Conectar la app\" trae tu cuenta acá", "Opens My account on the website: there, \"Connect the app\" brings your account here");
    public static string AccountSettingsTip => L("Dirección de la web y el token pegado a mano", "The website's address and the token pasted by hand");
    public static string SearchMore => L("Buscar más en la web", "Find more on the website");
    public static string SearchMoreTip => L("La biblioteca de la web: \"Usar en el coach\" suma una BO a tu lista", "The website's library: \"Use in the coach\" adds a BO to your list");
    public static string PasteCode => L("Pegar código", "Paste code");
    public static string PasteCodeTip => L("El código de 6 letras de una BO de la web (o el enlace de su página): se suma a tu lista", "The 6-letter code of a BO from the website (or the link to its page): it's added to your list");
    public static string RefreshList => L("Actualizar", "Refresh");
    public static string ListTip => L("Tocá una para usarla en vez de la automática (se baja al elegirla)", "Pick one to use it instead of the automatic one (it downloads when you pick it)");
    public static string AccountNotConnected => L("Conectá tu cuenta para elegir BO", "Connect your account to choose BOs");
    public static string AccountConnected => L("Cuenta conectada", "Account connected");
    public static string ListEmptyNoAccount => L("Cuando conectes tu cuenta, acá van a estar tus BO y las que sigas en la web.", "Once you connect your account, your BOs and the ones you follow on the website will be here.");
    public static string ListEmpty => L("Tu lista está vacía: armá una BO en la web o sumá una con Pegar código.", "Your list is empty: make a BO on the website or add one with Paste code.");
    public static string EntrySaved => L("guardada", "saved");
    public static string EntryNotSaved => L("se baja al usarla", "downloads when used");
    public static string CivAny => L("cualquier civ", "any civ");
    public static string OwnPrivate => L("tuya, privada", "yours, private");
    public static string Own => L("tuya", "yours");
    public static string AddBuildOrderTitle => L("Sumar una BO", "Add a BO");
    public static string AddBuildOrderPrompt => L("El código de la BO (6 letras y números) o el enlace de su página en la web:", "The BO's code (6 letters and numbers) or the link to its page on the website:");
    public static string ForeignLink => L("Ese enlace no es de BO Coach.", "That link isn't from BO Coach.");
    public static string NoAccountName => L("(sin nombre)", "(no name)");
    public static string ConnectToAdd => L("Conectá tu cuenta para sumar BO.", "Connect your account to add BOs.");
    public static string ConnectInWeb => L("En la web, tocá \"Conectar la app\" en Mi cuenta.", "On the website, press \"Connect the app\" in My account.");

    public static string AccountConnectedAs(string account) => L($"Conectada como {account}", $"Connected as {account}");
    public static string Downloading(string name) => L($"Bajando \"{name}\"…", $"Downloading \"{name}\"…");
    public static string Ready(string name) => L($"Lista: \"{name}\".", $"Ready: \"{name}\".");
    public static string DownloadFailed(string name, string error) => L($"No se pudo bajar \"{name}\": {error}", $"Couldn't download \"{name}\": {error}");
    public static string ListCount(int count) => L($"{count} BO en tu lista.", $"{count} BOs in your list.");
    public static string NotACode(string text) => L($"\"{text}\" no es un código de la web (son 6 letras y números).", $"\"{text}\" isn't a website code (they're 6 letters and numbers).");
    public static string ConnectFailed(string error) => L($"No se pudo conectar la cuenta: {error}", $"Couldn't connect the account: {error}");
    public static string ConfirmConnect(string account) => L($"¿Conectar BO Coach con la cuenta {account} de la web?", $"Connect BO Coach to the website account {account}?");
    public static string ConfirmAdd(string name, string author) => L($"¿Sumar \"{name}\" (subida por {author}) a tus BO?", $"Add \"{name}\" (uploaded by {author}) to your BOs?");
    public static string Added(string name) => L($"Sumada \"{name}\" a tus BO.", $"Added \"{name}\" to your BOs.");
    public static string AddFailed(string code, string error) => L($"No se pudo sumar {code}: {error}", $"Couldn't add {code}: {error}");

    // ---- En la partida ----
    public static string LivePlayerHeader => L("En la partida", "In the match");
    public static string LiveDevHeader => L("Lo que ve el coach", "What the coach sees");
    public static string LaneLabel => L("Línea", "Line");
    public static string LaneTip => L("Sobre qué línea actúan Ya está y Saltear (también los atajos)", "Which line Done and Skip act on (the hotkeys too)");
    public static string LaneFirstMissing => L("Lo primero que falta", "The first thing missing");
    public static string LaneVillagers => L("Aldeanos", "Villagers");
    public static string LaneBuild => L("Construir", "Build");
    public static string LaneResearch => L("Investigar", "Research");
    public static string LaneProduce => L("Producir", "Produce");
    public static string Back => L("◀ Atrás", "◀ Back");
    public static string Advance => L("Ya está ▶", "Done ▶");
    public static string Skip => L("Saltear", "Skip");
    public static string LogTip => L("Últimos eventos leídos del log", "Latest events read from the log");
    public static string LiveClock => L("Reloj ", "Clock ");
    public static string LiveAge => L("   Edad ", "   Age ");
    public static string LiveVillagers => L("   Aldeanos ", "   Villagers ");
    public static string LiveIdle => L("   Inactivos ", "   Idle ");
    public static string LivePopulation => L("   Población ", "   Population ");
    public static string ResourceFood => L("Comida", "Food");
    public static string ResourceWood => L("Madera", "Wood");
    public static string ResourceGold => L("Oro", "Gold");
    public static string ResourceStone => L("Piedra", "Stone");
    public static string NotRead => L("no se lee", "not read");
    public static string QueueNotRead => L("Cola de producción: no se lee (calibrá su caja)", "Production queue: not read (calibrate its box)");
    public static string QueueEmpty => L("Cola de producción: vacía", "Production queue: empty");
    public static string QueueHoused => L("sin población", "housed");
    public static string BuildOrderDone => L("BO terminada", "BO finished");

    public static string QueueItems(string items) => L($"Cola de producción: {items}", $"Production queue: {items}");
    public static string QueueWaiting(string name) => L($"{name} (espera)", $"{name} (waiting)");
    public static string ClockWithOffset(string clock, string started, string game) => L($"{clock} · la partida empezó a las {started} (juego {game})", $"{clock} · the match started at {started} (game {game})");
    public static string VillagersPlan(string seen, string guide, string bo) => L($"aldeanos: {seen} · GD {guide} (BO {bo})", $"villagers: {seen} · guide {guide} (BO {bo})");
    public static string VillagersSeen(int count) => L($"hay {count}", $"{count} now");
    public static string VillagersEstimated(string count) => L($"hay ~{count}", $"~{count} now");
    public static string VillagersUnknown => L("hay —", "— now");
    public static string PopulationEstimated(int population, int cap) => L($"{population}/{cap} (estimada)", $"{population}/{cap} (estimated)");
    public static string FoodFloor(int floor) => L($"mínimo en comida para no parar el centro urbano: {floor}", $"minimum on food to keep the town center going: {floor}");
    public static string Abandoned(string reason) => L($"BO abandonada: {reason} (\"◀ Atrás\" la retoma)", $"BO abandoned: {reason} (\"◀ Back\" resumes it)");
    public static string StepLine(int number, int total, string text, string state) => L($"Paso {number}/{total}: {text} ({state})", $"Step {number}/{total}: {text} ({state})");
    public static string BuildOrderDoneWith(string posture, bool derived) => L($"BO terminada. {posture}{(derived ? " (deducida de la BO)" : "")}", $"BO finished. {posture}{(derived ? " (deduced from the BO)" : "")}");
    public static string Clipped(string regions) => L($"Números cortados en: {regions}", $"Numbers cut off in: {regions}");
    public static string LiveError(string error) => $"Error: {error}";
    public static string LoadingNoName(string players, string map) => L($"Partida (pantalla de carga): {players}{map}. No sé cuál sos: poné \"Tu nombre\" en Ajustes.", $"Match (loading screen): {players}{map}. I don't know which one you are: set \"Your name\" in Settings.");
    public static string LoadingMe(string me, string allies, string rivals, string map) => L($"Partida (pantalla de carga): vos {me}{allies} · rivales {rivals}{map}", $"Match (loading screen): you {me}{allies} · rivals {rivals}{map}");
    public static string LoadingAllies(string allies) => L($" · aliados {allies}", $" · allies {allies}");
    public static string LoadingMap(string map) => L($" · mapa {map}", $" · map {map}");

    // ---- El coach: lo que dice en la barra de estado ----
    public static string StartCoach => L("▶ Empezar coach", "▶ Start coach");
    public static string StopCoach => L("■ Detener coach", "■ Stop coach");
    public static string ChooseToStart => L("Elegí una BO de tu lista (o pasá a Automática) para empezar el coach.", "Choose a BO from your list (or switch to Automatic) to start the coach.");
    public static string GameClosedStopped => L("El juego se cerró: coach detenido.", "The game closed: coach stopped.");
    public static string AutoNeedsBuildOrder => L("Coach automático: elegí una BO de tu lista (o pasá a Automática) para que arranque solo con el juego.", "Automatic coach: choose a BO from your list (or switch to Automatic) so it starts on its own with the game.");
    public static string CoachStopped => L("Coach detenido.", "Coach stopped.");
    public static string InvalidElo => L("Elo no válido: un número de 1 a 4000.", "Invalid Elo: a number from 1 to 4000.");
    public static string EloNeedsName => L("Poné tu nombre primero.", "Enter your name first.");
    public static string EloSearching => L("Buscando...", "Searching...");
    public static string GameCivFromBuildOrder => L("(la de la BO)", "(the BO's)");

    public static string HasErrors(string name, string error) => L($"\"{name}\" tiene errores ({error}): corregila en la web antes de empezar el coach.", $"\"{name}\" has errors ({error}): fix it on the website before starting the coach.");
    public static string CoachWaiting(string name, string civ, string language, string advance, string back, string skip, string overlay, string reminders) => L(
        $"Coach con \"{name}\"{civ}{language}, esperando la partida. Desde el juego: {advance} avanza, {back} retrocede, {skip} saltea, {overlay} oculta, {reminders} recordatorios en el mouse.",
        $"Coach with \"{name}\"{civ}{language}, waiting for the match. From the game: {advance} advances, {back} goes back, {skip} skips, {overlay} hides, {reminders} mouse reminders.");
    public static string PlayingAs(string civ) => L($" como {civ}", $" as {civ}");
    public static string ReportNotSaved(string error) => L($"No se pudo guardar el informe de la partida: {error}", $"Couldn't save the match report: {error}");
    public static string EloNotFound(string name) => L($"No encontré a {name} en el juego.", $"I couldn't find {name} in the game.");
    public static string EloFound(string alias, string solo, string team) => L($"Elo de {alias}: 1v1 {solo}, equipos {team}.", $"{alias}'s Elo: 1v1 {solo}, team {team}.");
    public static string NewVersionRunning(string name) => L($"Coach con \"{name}\", la versión nueva de la web.", $"Coach with \"{name}\", the new version from the website.");
    public static string SuggestedRunning(string name) => L($"Coach con \"{name}\", la sugerida para esta partida.", $"Coach with \"{name}\", the one suggested for this match.");
    public static string BackToBuildOrder(string name) => L($"Volví a \"{name}\".", $"Back to \"{name}\".");
    public static string AgainstFacing(string civ, string advice) => L($"Contra {civ} (enfrente): {advice}", $"Against {civ} (facing you): {advice}");
    public static string Suggested(string name, string reason, string key) => L($"Sugerida: {name} ({reason}). {key} para usarla", $"Suggested: {name} ({reason}). {key} to use it");
    public static string DownloadingSuggested(string name) => L($"Bajando {name}…", $"Downloading {name}…");
    public static string Switched(string name, string why, string back) => L($"Cambié a {name}{why}.{back}", $"Switched to {name}{why}.{back}");
    public static string SwitchReason(string reason) => $" ({reason})";
    public static string SwitchBack(string key, string name) => L($" {key} vuelve a {name}", $" {key} goes back to {name}");
    public static string LoadingNoPlayerName(string civ) => L($"Pantalla de carga: partida nueva. Poné \"Tu nombre\" para que el coach tome tu civ sola (sigue con {civ}).", $"Loading screen: new match. Set \"Your name\" so the coach picks up your civ on its own (staying with {civ}).");
    public static string LoadingPlayerNotFound(string name, string players, string civ) => L($"Pantalla de carga: no encontré a \"{name}\" entre {players}. Sigue con {civ}.", $"Loading screen: I couldn't find \"{name}\" among {players}. Staying with {civ}.");
    public static string LoadingCivUnknown(string text, string civ) => L($"no entendí tu civ (\"{text}\"), sigue con {civ}", $"I didn't understand your civ (\"{text}\"), staying with {civ}");
    public static string LoadingPlayingWith(string civ) => L($"jugás con {civ}", $"you play {civ}");
    public static string LoadingVs(string rivals) => L($" contra {rivals}", $" vs {rivals}");
    public static string LoadingOnMap(string map) => L($" en {map}", $" on {map}");
    public static string LoadingNote(string civ, string rivals, string map) => L($"Pantalla de carga: {civ}{rivals}{map}.", $"Loading screen: {civ}{rivals}{map}.");
    public static string GameInLanguage(string language) => L($", juego en {language}", $", game in {language}");
    public static string GameInLanguageNoOcr(string language) => L($", juego en {language} (Windows no tiene el OCR en castellano: lee con el inglés)", $", game in {language} (Windows doesn't have Spanish OCR: reading with the English one)");

    // ---- El registro de la partida ----
    public static string ShareNotWritten(string error) => L($"No se pudo anotar el registro para enviarlo: {error}", $"Couldn't queue the record for sending: {error}");
    public static string SharePending(int pending) => L($"Registro guardado para enviar después ({pending} en espera).", $"Record saved to send later ({pending} waiting).");
    public static string ShareSent => L("Registro de la partida enviado. ¡Gracias!", "Match record sent. Thanks!");
    public static string ShareFailed(string error) => L($"No se pudo enviar el registro: {error}", $"Couldn't send the record: {error}");
    public static string ForgetConfirm => L("¿Borrar de la web los registros de partida que subió esta PC? Los que tenés guardados acá no se tocan.", "Delete from the website the match records this PC uploaded? The ones saved here aren't touched.");
    public static string Forgotten(int count) => count == 1
        ? L("Se borró 1 registro de la web.", "1 record deleted from the website.")
        : L($"Se borraron {count} registros de la web.", $"{count} records deleted from the website.");
    public static string ForgetFailed(string error) => L($"No se pudo borrar: {error}", $"Couldn't delete: {error}");
    public static string RecordNotWritten(string error) => L($"No se pudo escribir el registro de la partida: {error}", $"Couldn't write the match record: {error}");

    // ---- El overlay ----
    public static string OverlayWaitingMatch => L("Esperando la partida…", "Waiting for the match…");
    public static string OverlayUsage(string usage, string ms) => L($"{usage} · {ms} ms por captura", $"{usage} · {ms} ms per capture");
    public static string OverlayStep => L("  ·  paso ", "  ·  step ");
    public static string OverlayRecordingEnded => L("Terminó la grabación.", "The recording ended.");
    public static string OverlayPaused(string key) => L($"En pausa: {key} para seguir.", $"Paused: {key} to continue.");
    public static string OverlayCantRead(string error) => L($"No se pudo leer la pantalla: {error}", $"Couldn't read the screen: {error}");
    public static string OverlayNotInFront => L("El juego no está al frente.", "The game isn't in front.");
    public static string OverlayWaitingGame => L("Esperando una partida…", "Waiting for a match…");
    public static string OverlayNoClock => L("No veo el reloj del juego: ¿menú, pantalla de carga o hay que calibrar?", "I can't see the game clock: menu, loading screen, or does it need calibrating?");
    public static string OverlayClipped(string regions) => L($"No entra el número de {regions}: agrandá la caja al calibrar.", $"The number in {regions} doesn't fit: make the box bigger when calibrating.");
    public static string MissingHudLetters => L("Falta data/fonts/hud_numbers.json: con el diseño nuevo del HUD no se leen los números", "data/fonts/hud_numbers.json is missing: the numbers can't be read with the new HUD layout");

    /// <summary>El nombre de una caja del HUD, para los avisos.</summary>
    public static string RegionLabel(string region) => region switch
    {
        Vision.Regions.RegionNames.Clock => L("Reloj", "Clock"),
        Vision.Regions.RegionNames.Food => ResourceFood,
        Vision.Regions.RegionNames.Wood => ResourceWood,
        Vision.Regions.RegionNames.Gold => ResourceGold,
        Vision.Regions.RegionNames.Stone => ResourceStone,
        Vision.Regions.RegionNames.Population => L("Población", "Population"),
        Vision.Regions.RegionNames.EventLog => L("Log de eventos", "Event log"),
        Vision.Regions.RegionNames.IdleVillagers => L("Inactivos", "Idle"),
        Vision.Regions.RegionNames.Age => L("Edad", "Age"),
        Vision.Regions.RegionNames.ProductionQueue => L("Cola de producción", "Production queue"),
        Vision.Regions.RegionNames.Scores => L("Puntajes", "Scores"),
        _ => region,
    };

    // ---- Ajustes ----
    public static string SettingsTitle => L("Ajustes", "Settings");
    public static string TabGame => L("Partida", "Match");
    public static string TabGameHud => L("Juego y HUD", "Game and HUD");
    public static string TabAdvanced => L("Avanzado", "Advanced");
    public static string TabDeveloper => L("Desarrollador", "Developer");
    public static string PlayerNameLabel => L("Tu nombre en el juego", "Your in-game name");
    public static string PlayerNameTip => L("Como sale en la lista de jugadores de la pantalla de carga. Ahí el coach lee con qué civ jugás (manda sobre la civ de acá abajo en esa partida), contra quién, y empieza la partida de cero", "As it appears in the player list on the loading screen. There the coach reads which civ you play (it overrides the civ below for that match), who against, and starts the match from zero");
    public static string Elo1v1Label => "Elo 1v1";
    public static string EloTeamLabel => L("Elo equipos", "Team Elo");
    public static string EloSearch => L("Buscar", "Search");
    public static string EloTip => L("Tu Elo de mapa aleatorio, para las estadísticas reales de cruces de la pantalla de carga y la BO sugerida. Buscar trae el de tu nombre de la API del juego: el nombre se manda solo al tocar el botón", "Your random map Elo, for the real matchup statistics on the loading screen and the suggested BO. Search gets the one for your name from the game's API: the name is only sent when you press the button");
    public static string EloSearchName => L("Buscar mi Elo en la API del juego", "Find my Elo in the game's API");
    public static string CivLabel => L("Civ de esta partida", "Civ for this match");
    public static string CivTip => L("El coach usa sus bonificaciones (costos, población de las casas, recursos iniciales). Por defecto, la civ que dice la BO. Se aplica al empezar el coach", "The coach uses its bonuses (costs, house population, starting resources). By default, the civ the BO says. It applies when the coach starts");
    public static string MaxVillagersLabel => L("Máx. aldeanos", "Max villagers");
    public static string MaxVillagersName => L("Máximo de aldeanos", "Maximum villagers");
    public static string MaxVillagersTip => L("Hasta cuántos aldeanos producen sin parar los centros urbanos (100 a 130 según la estrategia). La comida nunca baja de lo que necesitan. Se aplica al empezar el coach", "How many villagers the town centers keep producing up to (100 to 130 depending on the strategy). Food never drops below what they need. It applies when the coach starts");
    public static string CursorRemindersLabel => L("Recordatorios en el mouse", "Mouse reminders");
    public static string AutoStartLabel => L("Coach automático: arranca solo con el juego", "Automatic coach: starts on its own with the game");
    public static string GameLanguageLabel => L("Idioma del juego", "Game language");
    public static string GameLanguageTip => L("El log de eventos y la edad se leen con sus frases. Castellano de España y de Latinoamérica difieren en muchos nombres (en el juego: Opciones → Idioma). Se aplica al empezar el coach", "The event log and the age are read using its phrases. Spanish from Spain and Latin American Spanish differ in many names (in the game: Options → Language). It applies when the coach starts");
    public static string InstallMod => L("Instalar mod del HUD", "Install the HUD mod");
    public static string InstallModTip => L("Agrega al juego el mod local \"BoCoach HUD\": el panel de recursos con los números blancos grandes que el coach lee mucho mejor. Se puede volver a tocar sin problema", "Adds the local mod \"BoCoach HUD\" to the game: the resource panel with the big white numbers the coach reads much better. You can press it again without any problem");
    public static string ModMinimal => L("Mod mínimo", "Minimal mod");
    public static string ModMinimalTip => L("Mínimo: la barra del juego donde está, opaca, con los números blancos y una franja fina de color bajo cada ícono; el log, la cola y el menú quedan como los trae el juego. Sin marcar: el diseño completo (log en el rincón, cola debajo de la barra, casilleros de color). Después de cambiarlo, volvé a instalar el mod", "Minimal: the game's bar where it is, opaque, with white numbers and a thin color strip under each icon; the log, queue and menu stay as the game has them. Unchecked: the full layout (log in the corner, queue under the bar, color boxes). After changing it, install the mod again");
    public static string ModHint => L("El mod pone los números en blanco y grandes, que es lo que mejor lee el coach. Después de instalarlo, cerrá el juego y volvé a abrirlo.", "The mod makes the numbers big and white, which is what the coach reads best. After installing it, close the game and open it again.");
    public static string FirstRunButton => L("Primeros pasos", "First steps");
    public static string FirstRunTip => L("Idioma del juego, mod del HUD, una partida rápida contra 7 IA para calibrar y leer tu nombre en la pantalla de carga", "Game language, HUD mod, a quick match against 7 AIs to calibrate and read your name on the loading screen");
    public static string ScreenStatusLabel => L("Pantalla", "Screen");
    public static string WebAccountLabel => L("Cuenta de la web", "Web account");
    public static string WebAccountHint => L("Lo normal es conectarla con «Conectar cuenta». Acá se cambia la dirección de la web o se pega el token a mano.", "Normally you connect it with \"Connect account\". Here you can change the website's address or paste the token by hand.");
    public static string WebAccountButton => L("Dirección y token…", "Address and token…");
    public static string ReportsLabel => L("Informes de las partidas", "Match reports");
    public static string ReportsHint => L("Cada partida deja una página con los pasos contra la BO, el centro urbano parado y los recursos acumulados.", "Each match leaves a page with the steps against the BO, the idle town center and the floating resources.");
    public static string ReportsButton => L("Abrir la carpeta", "Open the folder");
    public static string ShareGamesLabel => L("Compartir mis partidas para mejorar el coach", "Share my games to improve the coach");
    public static string ShareExplanation => Coaching.RecordSharing.Explanation;
    public static string ForgetSharedButton => L("Borrar lo compartido…", "Delete what I shared…");
    public static string ForgetSharedTip => L("Le pide a la web que borre los registros de partida que subió esta PC", "Asks the website to delete the match records this PC uploaded");
    public static string SettingsApplyHint => L("La civ, el idioma y el máximo de aldeanos se aplican al empezar el coach.", "The civ, the game language and the villager maximum apply when the coach starts.");
    public static string CalibrateButton => L("Calibrar pantalla…", "Calibrate screen…");
    public static string RecordingsButton => L("Grabaciones", "Recordings");
    public static string AutoRecordLabel => L("Grabar cada partida sola", "Record every match automatically");
    public static string AutoRecordTip => L("Arranca a grabar cuando empieza una partida y corta cuando termina (o al detener el coach). No toca una grabación hecha a mano", "Starts recording when a match begins and stops when it ends (or when the coach stops). It doesn't touch a recording made by hand");
    public static string DiagnosticButton => L("Captura de diagnóstico", "Diagnostic capture");
    public static string DeveloperHint => L("Herramientas para revisar lo que lee el coach. El panel de abajo de la ventana principal también muestra más con este modo.", "Tools to check what the coach reads. The bottom panel of the main window also shows more in this mode.");

    // ---- Atajos ----
    public static string HotkeysTitle => L("Atajos", "Hotkeys");
    public static string HotkeysIntro => L("Andan aunque el foco esté en el juego. Para cambiar uno, hacé clic en la combinación y apretá la nueva (con Ctrl, Alt o Win). Escape cancela.", "They work even with the game in focus. To change one, click the combination and press the new one (with Ctrl, Alt or Win). Escape cancels.");
    public static string HotkeyRestore => L("Restaurar", "Restore");
    public static string HotkeyRestoreAll => L("Restaurar todos", "Restore all");
    public static string HotkeyStartStop => L("Empezar o detener el coach", "Start or stop the coach");
    public static string HotkeyAdvance => L("Ya está", "Done");
    public static string HotkeyBack => L("Atrás", "Back");
    public static string HotkeySkip => L("Saltear", "Skip");
    public static string HotkeyPause => L("Pausa", "Pause");
    public static string HotkeyOverlay => L("Ocultar el overlay", "Hide the overlay");
    public static string HotkeyCursorReminders => L("Recordatorios en el mouse", "Mouse reminders");
    public static string HotkeyAcceptSuggestion => L("Usar la BO sugerida", "Use the suggested BO");
    public static string HotkeyCalibrate => L("Calibrar a mano", "Calibrate by hand");
    public static string HotkeyRecord => L("Grabar", "Record");
    public static string HotkeyDebug => L("Debug del overlay", "Overlay debug");
    public static string HotkeyDiagnostic => L("Captura de diagnóstico", "Diagnostic capture");
    public static string HotkeyRepeated => L("Sin atajo (estaba repetido en settings.json)", "No hotkey (it was repeated in settings.json)");
    public static string HotkeyNeedsModifier => L("Tiene que llevar Ctrl, Alt o Win", "It must include Ctrl, Alt or Win");
    public static string HotkeyPressNew => L("Apretá la nueva…", "Press the new one…");
    public static string HotkeyNone => L("sin atajo", "no hotkey");

    public static string HotkeyTaken(string keys) => L($"Otro programa ya usa {keys}", $"Another program already uses {keys}");
    public static string HotkeyUsedBy(string? name) => L($"Ya la usa: {name}", $"Already used by: {name}");

    public static string TipStartStop(string key) => L($"Sigue la BO (la sugerida en Automática, o la elegida de tu lista) con el overlay sobre el juego. También con {key} desde el juego", $"Follows the BO (the suggested one in Automatic, or the one chosen from your list) with the overlay on top of the game. Also with {key} from the game");
    public static string TipFromGame(string key) => L($"También con {key} desde el juego", $"Also with {key} from the game");
    public static string TipCursorReminders(string key) => L($"Junto al mouse: una casa cuando quedan 3 lugares de población y un centro urbano cuando está parado (debajo de Máx. aldeanos). También con {key} desde el juego", $"Next to the mouse: a house when 3 population slots are left and a town center when it's idle (below Max villagers). Also with {key} from the game");
    public static string TipAutoStart(string key) => L($"El coach arranca solo cuando se abre el juego, con la última BO usada (en la pantalla de carga cambia a la sugerida si encaja mejor; {key} vuelve), y se detiene con el informe cuando el juego se cierra. Si lo detenés a mano, no vuelve a arrancar hasta que abras el juego de nuevo", $"The coach starts on its own when the game opens, with the last BO used (on the loading screen it switches to the suggested one if it fits better; {key} goes back), and stops with the report when the game closes. If you stop it by hand, it doesn't start again until you reopen the game");
    public static string TipDiagnostic(string key) => L($"Si algo no se lee bien: a los 2 s guarda la pantalla entera sin pérdida y un .txt con lo que encontró la app en %LOCALAPPDATA%\\BoCoach\\diagnostico, para mandar esa carpeta. También con {key} desde el juego", $"If something isn't read right: after 2 s it saves the whole screen losslessly and a .txt with what the app found in %LOCALAPPDATA%\\BoCoach\\diagnostico, so you can send that folder. Also with {key} from the game");
    public static string TipBack(string key) => L($"Deshace lo último que se completó o salteó ({key} desde el juego)", $"Undoes the last thing completed or skipped ({key} from the game)");
    public static string TipAdvance(string key) => L($"Da por hecho el paso actual, por ejemplo si el coach no vio que lo terminaste ({key} desde el juego)", $"Marks the current step as done, for example if the coach didn't see you finish it ({key} from the game)");
    public static string TipSkip(string key) => L($"Saltea el paso actual: no lo vas a hacer ({key} desde el juego)", $"Skips the current step: you won't do it ({key} from the game)");

    // ---- Primeros pasos ----
    public static string FirstRunTitle => L("Primeros pasos", "First steps");
    public static string FirstRunShareTitle => L("Decidí si compartís tus partidas", "Decide whether to share your games");
    public static string FirstRunIntro => L("Seis pasos para que el coach tenga tus BO y te lea bien. Se hacen una sola vez; si cambiás algo (otra PC, otra resolución, otro nombre), los volvés a abrir desde Ajustes.", "Six steps so the coach has your BOs and reads you well. You only do them once; if something changes (another PC, another resolution, another name), you can reopen them from Settings.");
    public static string FirstRunAccountTitle => L("Conectá tu cuenta de la web", "Connect your web account");
    public static string FirstRunAccountBody => L("Las build orders se arman y se eligen en la web. Se abre Mi cuenta en el navegador y ahí «Conectar la app» la trae acá.", "Build orders are made and chosen on the website. My account opens in your browser and there \"Connect the app\" brings it here.");
    public static string FirstRunLanguageTitle => L("Decime en qué idioma está el juego", "Tell me what language the game is in");
    public static string FirstRunLanguageBody => L("En el juego: Opciones → Idioma.", "In the game: Options → Language.");
    public static string FirstRunModTitle => L("Instalá el mod del HUD", "Install the HUD mod");
    public static string FirstRunModBody => L("Pone los aldeanos de cada recurso y el reloj en números blancos grandes, que es lo que mejor lee el coach. Con el mod, la app también se calibra sola. Después de instalarlo, cerrá el juego y volvé a abrirlo.", "It shows the villagers on each resource and the clock in big white numbers, which is what the coach reads best. With the mod, the app also calibrates itself. After installing it, close the game and open it again.");
    public static string FirstRunMatchTitle => L("Jugá una partida rápida contra 7 IA", "Play a quick match against 7 AIs");
    public static string FirstRunMatchBody => L("Tocá «Mirar la pantalla» y armá la partida en el juego. En la pantalla de carga leo la lista de jugadores, para saber cómo te llamás, y cuando arranca me calibro con el HUD: quedate unos segundos sin tapar la parte de arriba. Después podés salir.", "Press \"Watch the screen\" and set up the match in the game. On the loading screen I read the player list to learn your name, and when it starts I calibrate with the HUD: stay a few seconds without covering the top part. Then you can leave.");
    public static string FirstRunNameTitle => L("Decime tu nombre en el juego", "Tell me your in-game name");
    public static string FirstRunNameBody => L("Elegilo de la lista (sale de la pantalla de carga) y corregilo si el OCR erró una letra. Con tu nombre, en cada pantalla de carga el coach sabe tu civ y contra quién jugás.", "Choose it from the list (it comes from the loading screen) and fix it if the OCR got a letter wrong. With your name, on every loading screen the coach knows your civ and who you're playing against.");
    public static string FirstRunPlayersName => L("Jugadores de la pantalla de carga", "Players on the loading screen");
    public static string FirstRunFinish => L("Guardar mi nombre", "Save my name");
    public static string WatchStart => L("Mirar la pantalla", "Watch the screen");
    public static string WatchStop => L("Dejar de mirar", "Stop watching");
    public static string HudNotSeen => L("Todavía no vi el HUD del mod.", "I haven't seen the mod's HUD yet.");
    public static string HudCalibrated => L("Calibrada sola con el HUD del mod.", "Calibrated on its own with the mod's HUD.");
    public static string Watching => L("Mirando la pantalla cada 2 s. Empezá la partida rápida: la lista de jugadores sale en la pantalla de carga.", "Watching the screen every 2 s. Start the quick match: the player list shows on the loading screen.");
    public static string WatchDone => L("Listo: ya leí la pantalla de carga y calibré la pantalla. Podés salir de la partida.", "Done: I read the loading screen and calibrated the screen. You can leave the match.");
    public static string NoOcr => L("Windows no tiene el reconocimiento de texto (OCR) instalado: no puedo leer la pantalla de carga.", "Windows doesn't have text recognition (OCR) installed: I can't read the loading screen.");
    public static string CalibrateNext => L(" Cuando empiece la partida, me calibro solo con el HUD.", " When the match starts, I'll calibrate myself with the HUD.");

    public static string WatchFailed(string error) => L($"No pude mirar la pantalla: {error}", $"I couldn't watch the screen: {error}");
    public static string PlayersRead(int count) => L($"Leí la pantalla de carga: {count} jugadores. Elegí tu nombre en la lista.", $"I read the loading screen: {count} players. Choose your name in the list.");

    // ---- Informe ----
    public static string ReportTitle => L("Informe de la partida", "Match report");
    public static string ReportOpenFull => L("Abrir el informe completo", "Open the full report");

    // ---- Elegir el Elo ----
    public static string EloChoiceTitle => L("¿Cuál sos vos?", "Which one are you?");
    public static string EloChoiceBody => L("Hay varios jugadores con ese nombre. Elegí el tuyo (arriba, el que jugó más hace poco):", "There are several players with that name. Choose yours (at the top, the one who played most recently):");
    public static string EloChoiceUse => L("Usar este", "Use this one");

    public static string EloChoiceLine(string alias, string country, string solo, string team, string last) => L($"{alias}{country} · 1v1 {solo} · equipos {team}{last}", $"{alias}{country} · 1v1 {solo} · team {team}{last}");
    public static string EloChoiceLast(string date) => L($" · última partida {date}", $" · last match {date}");
    public static string EloChoiceNoMatches => L(" · sin partidas clasificatorias", " · no ranked matches");

    // ---- Cuenta de la web ----
    public static string WebAccountTitle => L("Cuenta de la web", "Web account");
    public static string WebAccountIntro => L("La cuenta trae tus BO de la web. Lo normal es conectarla con «Conectar cuenta»; acá podés cambiar la dirección de la web o pegar el token a mano.", "The account brings your BOs from the website. Normally you connect it with \"Connect account\"; here you can change the website's address or paste the token by hand.");
    public static string WebAddress => L("Dirección de la web", "Website address");
    public static string WebToken => L("Token de la app", "App token");
    public static string WebTokenBefore => L("Se crea en ", "It's created in ");
    public static string WebTokenLink => L("Mi cuenta de la web", "My account on the website");
    public static string WebTokenAfter => L(". Se guarda cifrado para tu usuario de Windows.", ". It's saved encrypted for your Windows user.");
    public static string WebLinksHint => L("Enlaces «Abrir en BO Coach»: para que el botón de la web abra la BO directo en esta app, Windows tiene que saber que los enlaces bocoach:// son de BO Coach.", "\"Open in BO Coach\" links: for the website's button to open the BO directly in this app, Windows has to know that bocoach:// links belong to BO Coach.");
    public static string WebLinksRegister => L("Registrar enlaces bocoach://", "Register bocoach:// links");
    public static string WebLinksRegistered => L("Registrados para esta app.", "Registered for this app.");
    public static string WebLinksNotRegistered => L("Sin registrar.", "Not registered.");
    public static string WebSave => L("Guardar", "Save");
    public static string WebNeedsHttps => L("La dirección de la web tiene que empezar con https://", "The website's address must start with https://");
    public static string WebBadToken => L("El token de la app empieza con bocoach_. Copialo de Mi cuenta en la web.", "The app token starts with bocoach_. Copy it from My account on the website.");

    // ---- Herramientas: calibrar, grabar y el mod ----
    public static string RecordStart => L("● Grabar partida", "● Record match");
    public static string RecordStop => L("■ Detener grabación", "■ Stop recording");
    public static string RecordingIndicator => L("● Grabando", "● Recording");
    public static string GameClosed => L("Juego cerrado o minimizado.", "Game closed or minimized.");
    public static string NotCalibrated => L("Pantalla sin calibrar: se usan posiciones aproximadas.", "Screen not calibrated: using approximate positions.");
    public static string HudNotFoundDefault => L("No encontré el HUD del mod en la pantalla: se usan posiciones aproximadas (instalá el mod o calibrá a mano).", "I couldn't find the mod's HUD on screen: using approximate positions (install the mod or calibrate by hand).");
    public static string ModTitle => L("Mod del HUD", "HUD mod");
    public static string ModGameNotFound => L("No encontré Age of Empires II: DE en las bibliotecas de Steam. Abrí el juego y volvé a probar.", "I couldn't find Age of Empires II: DE in your Steam libraries. Open the game and try again.");
    public static string ModRestart => L("Si el juego está abierto, cerralo y volvé a abrirlo. En Mods → Mods instalados tiene que figurar activo. Después calibrá la pantalla de nuevo: los cuadrados de los recursos cambian de aspecto.", "If the game is open, close it and open it again. In Mods → Installed mods it must show as active. Then calibrate the screen again: the resource squares change appearance.");
    public static string ModWarnings => L("Ojo:", "Watch out:");
    public static string ModNoPanels(string game) => L($"No encontré los paneles del juego en {game}.", $"I couldn't find the game's panels in {game}.");
    public static string ModNoProfiles(string folder) => L($"No encontré los perfiles del juego en {folder}: abrí el juego una vez y volvé a probar.", $"I couldn't find the game's profiles in {folder}: open the game once and try again.");
    public static string ModDescription(string layout) => L($"Panel de recursos propio ({layout}): fondo liso, íconos del juego, casillero del coach y campos negros para que BoCoach los lea.", $"Custom resource panel ({layout}): plain background, game icons, the coach's box and black fields so BoCoach can read them.");
    public static string ModInstalledProfile(string profile, string layout) => L($"Perfil {profile}: diseño {layout}.", $"Profile {profile}: {layout} layout.");
    public static string ModOff(string profile, string name) => L($"Perfil {profile}: \"{name}\" está apagado en Mods instalados. Activalo.", $"Profile {profile}: \"{name}\" is off in Installed mods. Turn it on.");
    public static string ModConflict(string profile, string title, int? priority, int? ours, string name) => L(
        $"Perfil {profile}: \"{title}\" también cambia el panel de recursos, el reloj, el log o la cola{(ours is null ? "." : $" y tiene más prioridad ({priority} contra {ours}).")} Poné \"{name}\" primero en Mods instalados o apagá ese mod.",
        $"Profile {profile}: \"{title}\" also changes the resource panel, the clock, the log or the queue{(ours is null ? "." : $" and has higher priority ({priority} vs {ours}).")} Put \"{name}\" first in Installed mods or turn that mod off.");
    public static string ModNoModsFolder => L("Ningún perfil del juego tiene carpeta de mods: abrí el juego, entrá una vez a Mods y volvé a probar.", "No game profile has a mods folder: open the game, go into Mods once and try again.");
    public static string DataLoadFailed(string error) => L($"No se pudieron cargar los datos del juego.\n\n{error}", $"The game data couldn't be loaded.\n\n{error}");
    public static string UnexpectedError(string error) => L($"Ocurrió un error inesperado.\n\n{error}", $"An unexpected error occurred.\n\n{error}");

    public static string RecordingSaved(int frames, string? folder) => L($"Grabación guardada: {frames} capturas en {folder}", $"Recording saved: {frames} captures in {folder}");
    public static string Recording(string elapsed, int frames, string? error) =>
        L($"Grabando {elapsed} · {frames} capturas", $"Recording {elapsed} · {frames} captures") + (error is null ? "" : L($" · último error: {error}", $" · last error: {error}"));
    public static string GameOpen(int width, int height, int x, int y) => L($"Juego abierto: {width}×{height} en ({x}, {y}).", $"Game open: {width}×{height} at ({x}, {y}).");
    public static string Calibrated(int width, int height, DateTime date) => L($"Calibrada para {width}×{height} ({date:d/M HH:mm}).", $"Calibrated for {width}×{height} ({date:d/M HH:mm}).");
    public static string CalibrateCaptureFailed(string error) => L($"No se pudo capturar la pantalla para calibrar sola: {error}", $"Couldn't capture the screen to calibrate automatically: {error}");
    public static string HudNotFoundManual(DateTime date) => L($"No encontré el HUD del mod en la pantalla: sigue la calibración manual ({date:d/M HH:mm}).", $"I couldn't find the mod's HUD on screen: keeping the manual calibration ({date:d/M HH:mm}).");
    public static string CalibratedWithLayout(string layout, int width, int height, int squares) => L($"Calibrada sola con el diseño {layout} para {width}×{height}: {squares} casilleros y el reloj", $"Calibrated automatically with the {layout} layout for {width}×{height}: {squares} boxes and the clock");
    public static string CalibratedAuto(int width, int height, int squares) => L($"Calibrada sola para {width}×{height}: {squares} cuadrados y el reloj", $"Calibrated automatically for {width}×{height}: {squares} squares and the clock");
    public static string CalibratedScale(string scale) => L($", HUD a escala {scale}", $", HUD at scale {scale}");
    public static string ModFailed(string error) => L($"No se pudo instalar el mod.\n\n{error}", $"The mod couldn't be installed.\n\n{error}");
    public static string ModInstalled(string name) => L($"Listo: el mod \"{name}\" quedó instalado.", $"Done: the mod \"{name}\" is installed.");
    public static string ModWarning(string warning) => $"• {warning}";
    public static string DiagnosticWaiting(string seconds) => L($"Captura de diagnóstico en {seconds} s: volvé al juego…", $"Diagnostic capture in {seconds} s: go back to the game…");
    public static string DiagnosticSaved(string? folder, string name) => L($"Captura de diagnóstico guardada en {folder} ({name}): mandá esa carpeta.", $"Diagnostic capture saved in {folder} ({name}): send that folder.");
    public static string DiagnosticFailed(string error) => L($"No se pudo guardar la captura de diagnóstico: {error}", $"Couldn't save the diagnostic capture: {error}");
}
