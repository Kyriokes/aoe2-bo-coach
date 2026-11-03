namespace BoCoach.App;

/// <summary>
/// Todos los textos de la app, en castellano y en un solo lugar: el XAML los toma con <c>{x:Static app:Texts.X}</c> y
/// los view models, directo; los que llevan datos son métodos. Para traducir la app se cambia este archivo, sin tocar el
/// XAML. Quedan afuera los que arma BoCoach.Core (pasos, overlay, informe), que comparte la web, y la calibración a
/// mano, que es de desarrollador.
/// </summary>
public static class Texts
{
    // ---- La app ----
    public static string AppName { get; } = "AoE2 BO Coach";
    public static string VersionTip { get; } = "Versión · commit · cuándo se compiló";
    public static string SupportButton { get; } = "Apoyar ☕";
    public static string SupportTip { get; } = "Invitame un café (Cafecito, PayPal, Ko-fi o Mercado Pago) y mirá las metas del proyecto en la web";
    public static string HotkeysButton { get; } = "Atajos";
    public static string HotkeysTip { get; } = "Ver y cambiar los atajos de teclado que andan desde el juego";
    public static string SettingsButton { get; } = "Ajustes ⚙";
    public static string SettingsTip { get; } = "Tu nombre, civ, idioma del juego, el mod del HUD y las demás opciones";
    public static string DevModeOn { get; } = "Modo desarrollador prendido.";
    public static string DevModeOff { get; } = "Modo desarrollador apagado.";
    public static string Accept { get; } = "Aceptar";
    public static string Cancel { get; } = "Cancelar";
    public static string Close { get; } = "Cerrar";
    public static string Yes { get; } = "Sí";
    public static string No { get; } = "No";
    public static string ConfirmTitle { get; } = "Confirmar";

    public static string HotkeysInUse(string keys) => $"Otro programa ya usa {keys}; usá los botones o cambialos en Atajos.";
    public static string HotkeysReady(string startStop, string advance) =>
        $"Atajos listos, también desde el juego: {startStop} empieza el coach y {advance} da por hecho el paso (todos en Atajos).";
    public static string HotkeyMissing(string name) => $"{name} (sin atajo)";

    // ---- El cuadro de estado ----
    public static string NoAccountTitle { get; } = "Conectá tu cuenta de la web";
    public static string NoAccountDetail { get; } = "Ahí se eligen las build orders. Se abre Mi cuenta en el navegador y «Conectar la app» la trae acá.";
    public static string WaitingGameTitle { get; } = "Esperando el juego";
    public static string WaitingGameDetail { get; } = "Abrí Age of Empires II: el coach arranca solo y se ve encima de la partida.";
    public static string WaitingMatchTitle { get; } = "Esperando la partida";
    public static string WaitingMatchGameOpen { get; } = "Empezá una partida: en la pantalla de carga el coach ve tu civ, tu rival y el mapa.";
    public static string WaitingMatchGameClosed { get; } = "Abrí Age of Empires II y empezá una partida: el coach se ve encima.";
    public static string StoppedTitle { get; } = "Coach detenido";
    public static string StoppedAutoOff { get; } = "El coach automático está apagado: tocá «Empezar ahora» cuando vayas a jugar.";
    public static string StoppedByYou { get; } = "Lo detuviste: tocá «Empezar ahora» para seguir. Solo vuelve a arrancar cuando abras el juego de nuevo.";
    public static string NeedsBuildOrder { get; } = "Elegí una BO de la lista de abajo, o pasá a Automática.";
    public static string InGameFallback { get; } = "El coach está siguiendo la partida.";
    public static string FinishedFallback { get; } = "Quedó guardado el informe de la partida.";
    public static string StartNow { get; } = "▶ Empezar ahora";
    public static string SeeReport { get; } = "Ver el informe";
    public static string ReportSeen { get; } = "Listo";

    public static string InGameTitle(string clock, string? civ) =>
        civ is null ? $"En partida · {clock}" : $"En partida · {clock} · {civ}";

    public static string FinishedTitle(string length) => $"Partida terminada · {length}";

    // ---- Los cuatro pasos ----
    public static string StepAccount { get; } = "Cuenta";
    public static string StepBuildOrder { get; } = "Build order";
    public static string StepPlay { get; } = "Jugar";
    public static string StepReport { get; } = "Informe";
    public static string StepAccountNone { get; } = "Sin conectar";
    public static string StepAccountLink { get; } = "Cuenta…";
    public static string StepBuildOrderAuto { get; } = "Automática: la sugerida para tu civ, mapa y rival";
    public static string StepBuildOrderMissing { get; } = "Falta elegir una de la lista";
    public static string StepPlayGameClosed { get; } = "Abrí el juego: el coach se ve encima.";
    public static string StepPlayGameOpen { get; } = "Juego abierto: empezá una partida.";
    public static string StepPlayInGame { get; } = "En partida.";
    public static string StepPlayNoName { get; } = "Falta tu nombre en el juego.";
    public static string StepReportNone { get; } = "Se arma solo al terminar una partida.";
    public static string StepReportOpen { get; } = "Abrir";
    public static string StepReportAll { get; } = "Ver todos";

    public static string StepBuildOrderManual(string name) => $"A mano: {name}";
    public static string StepReportLast(DateTime date, string? length) =>
        length is null ? $"Último: {date:d/M HH:mm}" : $"Último: {date:d/M HH:mm} · {length} de partida";
    public static string ReportStepsDone(int done, int total) => $"{done} de {total} pasos";
    public static string ReportIdle(string time) => $"centro urbano parado {time}";

    // ---- Build order ----
    public static string BuildOrderSection { get; } = "BUILD ORDER";
    public static string ModeAuto { get; } = "Automática: la sugerida para tu civ, mapa y rival en cada partida";
    public static string ModeManual { get; } = "Elegir de mi lista";
    public static string ConnectAccount { get; } = "Conectar cuenta";
    public static string ConnectAccountTip { get; } = "Abre Mi cuenta en la web: ahí \"Conectar la app\" trae tu cuenta acá";
    public static string AccountSettingsTip { get; } = "Dirección de la web y el token pegado a mano";
    public static string SearchMore { get; } = "Buscar más en la web";
    public static string SearchMoreTip { get; } = "La biblioteca de la web: \"Usar en el coach\" suma una BO a tu lista";
    public static string PasteCode { get; } = "Pegar código";
    public static string PasteCodeTip { get; } = "El código de 6 letras de una BO de la web (o el enlace de su página): se suma a tu lista";
    public static string RefreshList { get; } = "Actualizar";
    public static string ListTip { get; } = "Tocá una para usarla en vez de la automática (se baja al elegirla)";
    public static string AccountNotConnected { get; } = "Conectá tu cuenta para elegir BO";
    public static string AccountConnected { get; } = "Cuenta conectada";
    public static string ListEmptyNoAccount { get; } = "Cuando conectes tu cuenta, acá van a estar tus BO y las que sigas en la web.";
    public static string ListEmpty { get; } = "Tu lista está vacía: armá una BO en la web o sumá una con Pegar código.";
    public static string EntrySaved { get; } = "guardada";
    public static string EntryNotSaved { get; } = "se baja al usarla";
    public static string CivAny { get; } = "cualquier civ";
    public static string OwnPrivate { get; } = "tuya, privada";
    public static string Own { get; } = "tuya";
    public static string AddBuildOrderTitle { get; } = "Sumar una BO";
    public static string AddBuildOrderPrompt { get; } = "El código de la BO (6 letras y números) o el enlace de su página en la web:";
    public static string ForeignLink { get; } = "Ese enlace no es de BO Coach.";
    public static string NoAccountName { get; } = "(sin nombre)";
    public static string ConnectToAdd { get; } = "Conectá tu cuenta para sumar BO.";
    public static string ConnectInWeb { get; } = "En la web, tocá \"Conectar la app\" en Mi cuenta.";

    public static string AccountConnectedAs(string account) => $"Conectada como {account}";
    public static string Downloading(string name) => $"Bajando \"{name}\"…";
    public static string Ready(string name) => $"Lista: \"{name}\".";
    public static string DownloadFailed(string name, string error) => $"No se pudo bajar \"{name}\": {error}";
    public static string ListCount(int count) => $"{count} BO en tu lista.";
    public static string NotACode(string text) => $"\"{text}\" no es un código de la web (son 6 letras y números).";
    public static string ConnectFailed(string error) => $"No se pudo conectar la cuenta: {error}";
    public static string ConfirmConnect(string account) => $"¿Conectar BO Coach con la cuenta {account} de la web?";
    public static string ConfirmAdd(string name, string author) => $"¿Sumar \"{name}\" (subida por {author}) a tus BO?";
    public static string Added(string name) => $"Sumada \"{name}\" a tus BO.";
    public static string AddFailed(string code, string error) => $"No se pudo sumar {code}: {error}";

    // ---- En la partida ----
    public static string LivePlayerHeader { get; } = "En la partida";
    public static string LiveDevHeader { get; } = "Lo que ve el coach";
    public static string LaneLabel { get; } = "Línea";
    public static string LaneTip { get; } = "Sobre qué línea actúan Ya está y Saltear (también los atajos)";
    public static string LaneFirstMissing { get; } = "Lo primero que falta";
    public static string LaneVillagers { get; } = "Aldeanos";
    public static string LaneBuild { get; } = "Construir";
    public static string LaneResearch { get; } = "Investigar";
    public static string LaneProduce { get; } = "Producir";
    public static string Back { get; } = "◀ Atrás";
    public static string Advance { get; } = "Ya está ▶";
    public static string Skip { get; } = "Saltear";
    public static string LogTip { get; } = "Últimos eventos leídos del log";
    public static string LiveClock { get; } = "Reloj ";
    public static string LiveAge { get; } = "   Edad ";
    public static string LiveVillagers { get; } = "   Aldeanos ";
    public static string LiveIdle { get; } = "   Inactivos ";
    public static string LivePopulation { get; } = "   Población ";
    public static string ResourceFood { get; } = "Comida";
    public static string ResourceWood { get; } = "Madera";
    public static string ResourceGold { get; } = "Oro";
    public static string ResourceStone { get; } = "Piedra";
    public static string NotRead { get; } = "no se lee";
    public static string QueueNotRead { get; } = "Cola de producción: no se lee (calibrá su caja)";
    public static string QueueEmpty { get; } = "Cola de producción: vacía";
    public static string QueueHoused { get; } = "sin población";
    public static string BuildOrderDone { get; } = "BO terminada";

    public static string QueueItems(string items) => $"Cola de producción: {items}";
    public static string QueueWaiting(string name) => $"{name} (espera)";
    public static string ClockWithOffset(string clock, string started, string game) => $"{clock} · la partida empezó a las {started} (juego {game})";
    public static string VillagersPlan(string seen, string guide, string bo) => $"aldeanos: {seen} · GD {guide} (BO {bo})";
    public static string VillagersSeen(int count) => $"hay {count}";
    public static string VillagersEstimated(string count) => $"hay ~{count}";
    public static string VillagersUnknown { get; } = "hay —";
    public static string PopulationEstimated(int population, int cap) => $"{population}/{cap} (estimada)";
    public static string FoodFloor(int floor) => $"mínimo en comida para no parar el centro urbano: {floor}";
    public static string Abandoned(string reason) => $"BO abandonada: {reason} (\"◀ Atrás\" la retoma)";
    public static string StepLine(int number, int total, string text, string state) => $"Paso {number}/{total}: {text} ({state})";
    public static string BuildOrderDoneWith(string posture, bool derived) => $"BO terminada. {posture}{(derived ? " (deducida de la BO)" : "")}";
    public static string Clipped(string regions) => $"Números cortados en: {regions}";
    public static string LiveError(string error) => $"Error: {error}";
    public static string LoadingNoName(string players, string map) => $"Partida (pantalla de carga): {players}{map}. No sé cuál sos: poné \"Tu nombre\" en Ajustes.";
    public static string LoadingMe(string me, string allies, string rivals, string map) => $"Partida (pantalla de carga): vos {me}{allies} · rivales {rivals}{map}";
    public static string LoadingAllies(string allies) => $" · aliados {allies}";
    public static string LoadingMap(string map) => $" · mapa {map}";

    // ---- El coach: lo que dice en la barra de estado ----
    public static string StartCoach { get; } = "▶ Empezar coach";
    public static string StopCoach { get; } = "■ Detener coach";
    public static string ChooseToStart { get; } = "Elegí una BO de tu lista (o pasá a Automática) para empezar el coach.";
    public static string GameClosedStopped { get; } = "El juego se cerró: coach detenido.";
    public static string AutoNeedsBuildOrder { get; } = "Coach automático: elegí una BO de tu lista (o pasá a Automática) para que arranque solo con el juego.";
    public static string CoachStopped { get; } = "Coach detenido.";
    public static string InvalidElo { get; } = "Elo no válido: un número de 1 a 4000.";
    public static string EloNeedsName { get; } = "Poné tu nombre primero.";
    public static string EloSearching { get; } = "Buscando...";
    public static string GameCivFromBuildOrder { get; } = "(la de la BO)";

    public static string HasErrors(string name, string error) => $"\"{name}\" tiene errores ({error}): corregila en la web antes de empezar el coach.";
    public static string CoachWaiting(string name, string civ, string language, string advance, string back, string skip, string overlay, string reminders) =>
        $"Coach con \"{name}\"{civ}{language}, esperando la partida. Desde el juego: {advance} avanza, {back} retrocede, {skip} saltea, {overlay} oculta, {reminders} recordatorios en el mouse.";
    public static string PlayingAs(string civ) => $" como {civ}";
    public static string ReportNotSaved(string error) => $"No se pudo guardar el informe de la partida: {error}";
    public static string EloNotFound(string name) => $"No encontré a {name} en el juego.";
    public static string EloFound(string alias, string solo, string team) => $"Elo de {alias}: 1v1 {solo}, equipos {team}.";
    public static string NewVersionRunning(string name) => $"Coach con \"{name}\", la versión nueva de la web.";
    public static string SuggestedRunning(string name) => $"Coach con \"{name}\", la sugerida para esta partida.";
    public static string BackToBuildOrder(string name) => $"Volví a \"{name}\".";
    public static string AgainstFacing(string civ, string advice) => $"Contra {civ} (enfrente): {advice}";
    public static string Suggested(string name, string reason, string key) => $"Sugerida: {name} ({reason}). {key} para usarla";
    public static string DownloadingSuggested(string name) => $"Bajando {name}…";
    public static string Switched(string name, string why, string back) => $"Cambié a {name}{why}.{back}";
    public static string SwitchReason(string reason) => $" ({reason})";
    public static string SwitchBack(string key, string name) => $" {key} vuelve a {name}";
    public static string LoadingNoPlayerName(string civ) => $"Pantalla de carga: partida nueva. Poné \"Tu nombre\" para que el coach tome tu civ sola (sigue con {civ}).";
    public static string LoadingPlayerNotFound(string name, string players, string civ) => $"Pantalla de carga: no encontré a \"{name}\" entre {players}. Sigue con {civ}.";
    public static string LoadingCivUnknown(string text, string civ) => $"no entendí tu civ (\"{text}\"), sigue con {civ}";
    public static string LoadingPlayingWith(string civ) => $"jugás con {civ}";
    public static string LoadingVs(string rivals) => $" contra {rivals}";
    public static string LoadingOnMap(string map) => $" en {map}";
    public static string LoadingNote(string civ, string rivals, string map) => $"Pantalla de carga: {civ}{rivals}{map}.";
    public static string GameInLanguage(string language) => $", juego en {language}";
    public static string GameInLanguageNoOcr(string language) => $", juego en {language} (Windows no tiene el OCR en castellano: lee con el inglés)";

    // ---- Ajustes ----
    public static string SettingsTitle { get; } = "Ajustes";
    public static string TabGame { get; } = "Partida";
    public static string TabGameHud { get; } = "Juego y HUD";
    public static string TabAdvanced { get; } = "Avanzado";
    public static string TabDeveloper { get; } = "Desarrollador";
    public static string PlayerNameLabel { get; } = "Tu nombre en el juego";
    public static string PlayerNameTip { get; } = "Como sale en la lista de jugadores de la pantalla de carga. Ahí el coach lee con qué civ jugás (manda sobre la civ de acá abajo en esa partida), contra quién, y empieza la partida de cero";
    public static string Elo1v1Label { get; } = "Elo 1v1";
    public static string EloTeamLabel { get; } = "Elo equipos";
    public static string EloSearch { get; } = "Buscar";
    public static string EloTip { get; } = "Tu Elo de mapa aleatorio, para las estadísticas reales de cruces de la pantalla de carga y la BO sugerida. Buscar trae el de tu nombre de la API del juego: el nombre se manda solo al tocar el botón";
    public static string EloSearchName { get; } = "Buscar mi Elo en la API del juego";
    public static string CivLabel { get; } = "Civ de esta partida";
    public static string CivTip { get; } = "El coach usa sus bonificaciones (costos, población de las casas, recursos iniciales). Por defecto, la civ que dice la BO. Se aplica al empezar el coach";
    public static string MaxVillagersLabel { get; } = "Máx. aldeanos";
    public static string MaxVillagersName { get; } = "Máximo de aldeanos";
    public static string MaxVillagersTip { get; } = "Hasta cuántos aldeanos producen sin parar los centros urbanos (100 a 130 según la estrategia). La comida nunca baja de lo que necesitan. Se aplica al empezar el coach";
    public static string CursorRemindersLabel { get; } = "Recordatorios en el mouse";
    public static string AutoStartLabel { get; } = "Coach automático: arranca solo con el juego";
    public static string GameLanguageLabel { get; } = "Idioma del juego";
    public static string GameLanguageTip { get; } = "El log de eventos y la edad se leen con sus frases. Castellano de España y de Latinoamérica difieren en muchos nombres (en el juego: Opciones → Idioma). Se aplica al empezar el coach";
    public static string InstallMod { get; } = "Instalar mod del HUD";
    public static string InstallModTip { get; } = "Agrega al juego el mod local \"BoCoach HUD\": el panel de recursos con los números blancos grandes que el coach lee mucho mejor. Se puede volver a tocar sin problema";
    public static string ModMinimal { get; } = "Mod mínimo";
    public static string ModMinimalTip { get; } = "Mínimo: la barra del juego donde está, opaca, con los números blancos y una franja fina de color bajo cada ícono; el log, la cola y el menú quedan como los trae el juego. Sin marcar: el diseño completo (log en el rincón, cola debajo de la barra, casilleros de color). Después de cambiarlo, volvé a instalar el mod";
    public static string ModHint { get; } = "El mod pone los números en blanco y grandes, que es lo que mejor lee el coach. Después de instalarlo, cerrá el juego y volvé a abrirlo.";
    public static string FirstRunButton { get; } = "Primeros pasos";
    public static string FirstRunTip { get; } = "Idioma del juego, mod del HUD, una partida rápida contra 7 IA para calibrar y leer tu nombre en la pantalla de carga";
    public static string ScreenStatusLabel { get; } = "Pantalla";
    public static string WebAccountLabel { get; } = "Cuenta de la web";
    public static string WebAccountHint { get; } = "Lo normal es conectarla con «Conectar cuenta». Acá se cambia la dirección de la web o se pega el token a mano.";
    public static string WebAccountButton { get; } = "Dirección y token…";
    public static string ReportsLabel { get; } = "Informes de las partidas";
    public static string ReportsHint { get; } = "Cada partida deja una página con los pasos contra la BO, el centro urbano parado y los recursos acumulados.";
    public static string ReportsButton { get; } = "Abrir la carpeta";
    public static string SettingsApplyHint { get; } = "La civ, el idioma y el máximo de aldeanos se aplican al empezar el coach.";
    public static string CalibrateButton { get; } = "Calibrar pantalla…";
    public static string RecordingsButton { get; } = "Grabaciones";
    public static string AutoRecordLabel { get; } = "Grabar cada partida sola";
    public static string AutoRecordTip { get; } = "Arranca a grabar cuando empieza una partida y corta cuando termina (o al detener el coach). No toca una grabación hecha a mano";
    public static string DiagnosticButton { get; } = "Captura de diagnóstico";
    public static string DeveloperHint { get; } = "Herramientas para revisar lo que lee el coach. El panel de abajo de la ventana principal también muestra más con este modo.";

    // ---- Atajos ----
    public static string HotkeysTitle { get; } = "Atajos";
    public static string HotkeysIntro { get; } = "Andan aunque el foco esté en el juego. Para cambiar uno, hacé clic en la combinación y apretá la nueva (con Ctrl, Alt o Win). Escape cancela.";
    public static string HotkeyRestore { get; } = "Restaurar";
    public static string HotkeyRestoreAll { get; } = "Restaurar todos";
    public static string HotkeyStartStop { get; } = "Empezar o detener el coach";
    public static string HotkeyAdvance { get; } = "Ya está";
    public static string HotkeyBack { get; } = "Atrás";
    public static string HotkeySkip { get; } = "Saltear";
    public static string HotkeyPause { get; } = "Pausa";
    public static string HotkeyOverlay { get; } = "Ocultar el overlay";
    public static string HotkeyCursorReminders { get; } = "Recordatorios en el mouse";
    public static string HotkeyAcceptSuggestion { get; } = "Usar la BO sugerida";
    public static string HotkeyCalibrate { get; } = "Calibrar a mano";
    public static string HotkeyRecord { get; } = "Grabar";
    public static string HotkeyDebug { get; } = "Debug del overlay";
    public static string HotkeyDiagnostic { get; } = "Captura de diagnóstico";
    public static string HotkeyRepeated { get; } = "Sin atajo (estaba repetido en settings.json)";
    public static string HotkeyNeedsModifier { get; } = "Tiene que llevar Ctrl, Alt o Win";
    public static string HotkeyPressNew { get; } = "Apretá la nueva…";

    public static string HotkeyTaken(string keys) => $"Otro programa ya usa {keys}";
    public static string HotkeyUsedBy(string? name) => $"Ya la usa: {name}";

    public static string TipStartStop(string key) => $"Sigue la BO (la sugerida en Automática, o la elegida de tu lista) con el overlay sobre el juego. También con {key} desde el juego";
    public static string TipFromGame(string key) => $"También con {key} desde el juego";
    public static string TipCursorReminders(string key) => $"Junto al mouse: una casa cuando quedan 3 lugares de población y un centro urbano cuando está parado (debajo de Máx. aldeanos). También con {key} desde el juego";
    public static string TipAutoStart(string key) => $"El coach arranca solo cuando se abre el juego, con la última BO usada (en la pantalla de carga cambia a la sugerida si encaja mejor; {key} vuelve), y se detiene con el informe cuando el juego se cierra. Si lo detenés a mano, no vuelve a arrancar hasta que abras el juego de nuevo";
    public static string TipDiagnostic(string key) => $"Si algo no se lee bien: a los 2 s guarda la pantalla entera sin pérdida y un .txt con lo que encontró la app en %LOCALAPPDATA%\\BoCoach\\diagnostico, para mandar esa carpeta. También con {key} desde el juego";
    public static string TipBack(string key) => $"Deshace lo último que se completó o salteó ({key} desde el juego)";
    public static string TipAdvance(string key) => $"Da por hecho el paso actual, por ejemplo si el coach no vio que lo terminaste ({key} desde el juego)";
    public static string TipSkip(string key) => $"Saltea el paso actual: no lo vas a hacer ({key} desde el juego)";

    // ---- Primeros pasos ----
    public static string FirstRunTitle { get; } = "Primeros pasos";
    public static string FirstRunIntro { get; } = "Cinco pasos para que el coach tenga tus BO y te lea bien. Se hacen una sola vez; si cambiás algo (otra PC, otra resolución, otro nombre), los volvés a abrir desde Ajustes.";
    public static string FirstRunAccountTitle { get; } = "Conectá tu cuenta de la web";
    public static string FirstRunAccountBody { get; } = "Las build orders se arman y se eligen en la web. Se abre Mi cuenta en el navegador y ahí «Conectar la app» la trae acá.";
    public static string FirstRunLanguageTitle { get; } = "Decime en qué idioma está el juego";
    public static string FirstRunLanguageBody { get; } = "En el juego: Opciones → Idioma.";
    public static string FirstRunModTitle { get; } = "Instalá el mod del HUD";
    public static string FirstRunModBody { get; } = "Pone los aldeanos de cada recurso y el reloj en números blancos grandes, que es lo que mejor lee el coach. Con el mod, la app también se calibra sola. Después de instalarlo, cerrá el juego y volvé a abrirlo.";
    public static string FirstRunMatchTitle { get; } = "Jugá una partida rápida contra 7 IA";
    public static string FirstRunMatchBody { get; } = "Tocá «Mirar la pantalla» y armá la partida en el juego. En la pantalla de carga leo la lista de jugadores, para saber cómo te llamás, y cuando arranca me calibro con el HUD: quedate unos segundos sin tapar la parte de arriba. Después podés salir.";
    public static string FirstRunNameTitle { get; } = "Decime tu nombre en el juego";
    public static string FirstRunNameBody { get; } = "Elegilo de la lista (sale de la pantalla de carga) y corregilo si el OCR erró una letra. Con tu nombre, en cada pantalla de carga el coach sabe tu civ y contra quién jugás.";
    public static string FirstRunPlayersName { get; } = "Jugadores de la pantalla de carga";
    public static string FirstRunFinish { get; } = "Guardar mi nombre";
    public static string WatchStart { get; } = "Mirar la pantalla";
    public static string WatchStop { get; } = "Dejar de mirar";
    public static string HudNotSeen { get; } = "Todavía no vi el HUD del mod.";
    public static string HudCalibrated { get; } = "Calibrada sola con el HUD del mod.";
    public static string Watching { get; } = "Mirando la pantalla cada 2 s. Empezá la partida rápida: la lista de jugadores sale en la pantalla de carga.";
    public static string WatchDone { get; } = "Listo: ya leí la pantalla de carga y calibré la pantalla. Podés salir de la partida.";
    public static string NoOcr { get; } = "Windows no tiene el reconocimiento de texto (OCR) instalado: no puedo leer la pantalla de carga.";
    public static string CalibrateNext { get; } = " Cuando empiece la partida, me calibro solo con el HUD.";

    public static string WatchFailed(string error) => $"No pude mirar la pantalla: {error}";
    public static string PlayersRead(int count) => $"Leí la pantalla de carga: {count} jugadores. Elegí tu nombre en la lista.";

    // ---- Informe ----
    public static string ReportTitle { get; } = "Informe de la partida";
    public static string ReportOpenFull { get; } = "Abrir el informe completo";

    // ---- Elegir el Elo ----
    public static string EloChoiceTitle { get; } = "¿Cuál sos vos?";
    public static string EloChoiceBody { get; } = "Hay varios jugadores con ese nombre. Elegí el tuyo (arriba, el que jugó más hace poco):";
    public static string EloChoiceUse { get; } = "Usar este";

    public static string EloChoiceLine(string alias, string country, string solo, string team, string last) => $"{alias}{country} · 1v1 {solo} · equipos {team}{last}";
    public static string EloChoiceLast(string date) => $" · última partida {date}";
    public static string EloChoiceNoMatches { get; } = " · sin partidas clasificatorias";

    // ---- Cuenta de la web ----
    public static string WebAccountTitle { get; } = "Cuenta de la web";
    public static string WebAccountIntro { get; } = "La cuenta trae tus BO de la web. Lo normal es conectarla con «Conectar cuenta»; acá podés cambiar la dirección de la web o pegar el token a mano.";
    public static string WebAddress { get; } = "Dirección de la web";
    public static string WebToken { get; } = "Token de la app";
    public static string WebTokenBefore { get; } = "Se crea en ";
    public static string WebTokenLink { get; } = "Mi cuenta de la web";
    public static string WebTokenAfter { get; } = ". Se guarda cifrado para tu usuario de Windows.";
    public static string WebLinksHint { get; } = "Enlaces «Abrir en BO Coach»: para que el botón de la web abra la BO directo en esta app, Windows tiene que saber que los enlaces bocoach:// son de BO Coach.";
    public static string WebLinksRegister { get; } = "Registrar enlaces bocoach://";
    public static string WebLinksRegistered { get; } = "Registrados para esta app.";
    public static string WebLinksNotRegistered { get; } = "Sin registrar.";
    public static string WebSave { get; } = "Guardar";
    public static string WebNeedsHttps { get; } = "La dirección de la web tiene que empezar con https://";
    public static string WebBadToken { get; } = "El token de la app empieza con bocoach_. Copialo de Mi cuenta en la web.";

    // ---- Herramientas: calibrar, grabar y el mod ----
    public static string RecordStart { get; } = "● Grabar partida";
    public static string RecordStop { get; } = "■ Detener grabación";
    public static string RecordingIndicator { get; } = "● Grabando";
    public static string GameClosed { get; } = "Juego cerrado o minimizado.";
    public static string NotCalibrated { get; } = "Pantalla sin calibrar: se usan posiciones aproximadas.";
    public static string HudNotFoundDefault { get; } = "No encontré el HUD del mod en la pantalla: se usan posiciones aproximadas (instalá el mod o calibrá a mano).";
    public static string ModTitle { get; } = "Mod del HUD";
    public static string ModGameNotFound { get; } = "No encontré Age of Empires II: DE en las bibliotecas de Steam. Abrí el juego y volvé a probar.";
    public static string ModRestart { get; } = "Si el juego está abierto, cerralo y volvé a abrirlo. En Mods → Mods instalados tiene que figurar activo. Después calibrá la pantalla de nuevo: los cuadrados de los recursos cambian de aspecto.";
    public static string ModWarnings { get; } = "Ojo:";

    public static string RecordingSaved(int frames, string? folder) => $"Grabación guardada: {frames} capturas en {folder}";
    public static string Recording(string elapsed, int frames, string? error) =>
        $"Grabando {elapsed} · {frames} capturas" + (error is null ? "" : $" · último error: {error}");
    public static string GameOpen(int width, int height, int x, int y) => $"Juego abierto: {width}×{height} en ({x}, {y}).";
    public static string Calibrated(int width, int height, DateTime date) => $"Calibrada para {width}×{height} ({date:d/M HH:mm}).";
    public static string CalibrateCaptureFailed(string error) => $"No se pudo capturar la pantalla para calibrar sola: {error}";
    public static string HudNotFoundManual(DateTime date) => $"No encontré el HUD del mod en la pantalla: sigue la calibración manual ({date:d/M HH:mm}).";
    public static string CalibratedWithLayout(string layout, int width, int height, int squares) => $"Calibrada sola con el diseño {layout} para {width}×{height}: {squares} casilleros y el reloj";
    public static string CalibratedAuto(int width, int height, int squares) => $"Calibrada sola para {width}×{height}: {squares} cuadrados y el reloj";
    public static string CalibratedScale(string scale) => $", HUD a escala {scale}";
    public static string ModFailed(string error) => $"No se pudo instalar el mod.\n\n{error}";
    public static string ModInstalled(string name) => $"Listo: el mod \"{name}\" quedó instalado.";
    public static string ModWarning(string warning) => $"• {warning}";
    public static string DiagnosticWaiting(string seconds) => $"Captura de diagnóstico en {seconds} s: volvé al juego…";
    public static string DiagnosticSaved(string? folder, string name) => $"Captura de diagnóstico guardada en {folder} ({name}): mandá esa carpeta.";
    public static string DiagnosticFailed(string error) => $"No se pudo guardar la captura de diagnóstico: {error}";
}
