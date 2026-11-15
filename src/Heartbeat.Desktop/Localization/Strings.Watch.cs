namespace Heartbeat.Desktop.Localization;

/// <summary>Textos de la pantalla del reloj (FC en vivo y Samsung Health) y de las lecturas que vienen de él.</summary>
public static partial class Strings
{
    private static void AddWatch(Dictionary<string, (string En, string Es)> t)
    {
        t["nav.watch"] = ("Watch", "Reloj");
        t["watch.heading"] = ("Galaxy Watch 5", "Galaxy Watch 5");
        t["watch.disclaimer"] = ("Watch data, not clinically validated", "Datos del reloj, no validados clínicamente");
        t["watch.tabLive"] = ("Live heart rate", "FC en vivo");
        t["watch.tabHealth"] = ("Samsung Health", "Samsung Health");
        t["watch.noReadings"] = ("No readings yet", "Sin lecturas todavía");
        t["watch.measuredAt"] = ("measured at {0}", "medida a las {0}");
        t["watch.lastSaved"] = ("last saved reading · {0}", "última lectura guardada · {0}");
        t["watch.status.live"] = ("live", "en vivo");
        t["watch.status.stale"] = ("no data for more than 10 s", "sin datos hace más de 10 s");
        t["watch.status.connecting"] = ("connecting…", "conectando…");
        t["watch.status.reconnecting"] = ("reconnecting…", "reconectando…");
        t["watch.status.failed"] = ("cannot reach the server; retrying…", "sin conexión con el servidor; reintentando…");
        t["watch.status.stopped"] = ("stopped", "detenido");
        t["watch.status.offline"] = ("offline", "sin enlace");
        t["watch.noData.stale"] = ("No watch data: open the app on the watch and keep the screen on.", "Sin datos del reloj: abre la app en el reloj y mantén la pantalla encendida.");
        t["watch.noData.offline"] = ("No watch data: the link is off. Showing the last saved reading.", "Sin datos del reloj: no hay enlace. Se muestra la última lectura guardada.");
        t["watch.readingFromWatch"] = ("Watch reading", "Lectura del reloj");
        t["watch.legend"] = ("Diamond markers are readings from the watch.", "Los rombos son lecturas del reloj.");

        t["watch.refresh"] = ("Refresh", "Actualizar");
        t["watch.loading"] = ("Loading…", "Cargando…");
        t["watch.latest"] = ("Latest values", "Últimos valores");
        t["watch.activity"] = ("Activity", "Actividad");
        t["watch.lastNight"] = ("Last night", "Última noche");
        t["watch.skinTemp"] = ("Skin temperature", "Temperatura de piel");
        t["watch.weight"] = ("Weight", "Peso");
        t["watch.measuredLabel"] = ("Measured: {0}", "Medido: {0}");
        t["watch.stepsToday"] = ("Steps today: {0}", "Pasos de hoy: {0}");
        t["watch.col.date"] = ("DATE", "FECHA");
        t["watch.col.steps"] = ("STEPS", "PASOS");
        t["watch.col.distance"] = ("DISTANCE", "DISTANCIA");
        t["watch.col.calories"] = ("ACTIVE CALORIES", "CALORÍAS ACTIVAS");
        t["watch.col.minutes"] = ("ACTIVE MINUTES", "MINUTOS ACTIVOS");
        t["watch.sleepStages"] = ("Awake {0} min · Light {1} min · Deep {2} min · REM {3} min", "Despierto {0} min · Ligero {1} min · Profundo {2} min · REM {3} min");
        t["watch.sync.none"] = ("Last Samsung Health sync: no data", "Última sincronización de Samsung Health: sin datos");
        t["watch.sync.ago"] = ("Last Samsung Health sync: {0} min ago", "Última sincronización de Samsung Health: hace {0} min");
        t["watch.health.offline"] = ("Offline: Samsung Health data needs the link to the server.", "Sin enlace: los datos de Samsung Health necesitan conexión con el servidor.");
        t["watch.health.unavailable"] = ("The Samsung Health data server is not responding", "El servidor de datos de Samsung Health no responde");
        t["watch.health.signIn"] = ("Sign in online (CrewApi:Mode = Live) to see Samsung Health data.", "Inicia sesión con conexión al servidor (CrewApi:Mode = Live) para ver los datos de Samsung Health.");
        t["watch.health.error"] = ("Error {0}: {1}", "Error {0}: {1}");
        t["watch.health.connect"] = ("Could not connect to the backend ({0})", "No se pudo conectar con el backend ({0})");
    }
}
