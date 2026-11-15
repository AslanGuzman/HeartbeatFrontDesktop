namespace Heartbeat.Desktop.Localization;

/// <summary>Textos de la pantalla de visión y SANS (HB-FE-04). Sin afirmaciones diagnósticas.</summary>
public static partial class Strings
{
    private static void AddVision(Dictionary<string, (string En, string Es)> t)
    {
        t["nav.vision"] = ("Vision", "Visión");
        t["vision.heading"] = ("Eye pressure and vision", "Presión ocular y visión");
        t["vision.intro"] = ("Log your intraocular pressure (IOP) and visual acuity. They are saved on this computer and the chart shows how each one changes over the last 30 days.", "Anota tu presión intraocular (PIO) y tu agudeza visual. Se guardan en este equipo y la gráfica muestra cómo cambia cada una en los últimos 30 días.");
        t["vision.logHeading"] = ("New reading", "Nueva lectura");
        t["vision.iopLabel"] = ("Eye pressure (mmHg)", "Presión ocular (mmHg)");
        t["vision.iopAria"] = ("Intraocular pressure in millimeters of mercury", "Presión intraocular en milímetros de mercurio");
        t["vision.acuityLabel"] = ("Visual acuity (logMAR)", "Agudeza visual (logMAR)");
        t["vision.acuityAria"] = ("Visual acuity in logMAR", "Agudeza visual en logMAR");
        t["vision.add"] = ("Save reading", "Guardar lectura");
        t["vision.saved"] = ("Reading saved on this computer.", "Lectura guardada en este equipo.");
        t["vision.cardsAria"] = ("Eye pressure and visual acuity", "Presión ocular y agudeza visual");
        t["vision.noData"] = ("No readings yet", "Aún no hay lecturas");
        t["vision.iop"] = ("Eye pressure (IOP)", "Presión ocular (PIO)");
        t["vision.acuity"] = ("Visual acuity", "Agudeza visual");
        t["vision.chartTitle"] = ("{0} over the last 30 days", "{0} en los últimos 30 días");

        t["vision.err.empty"] = ("Enter at least one value.", "Escribe al menos un valor.");
        t["vision.err.iop"] = ("Eye pressure must be a number greater than 0 and up to {0} mmHg.", "La presión ocular debe ser un número mayor que 0 y de hasta {0} mmHg.");
        t["vision.err.acuity"] = ("Visual acuity must be a number between {0} and {1} logMAR.", "La agudeza visual debe ser un número entre {0} y {1} logMAR.");

        t["vision.trend.none"] = ("No readings in the last 30 days.", "Sin lecturas en los últimos 30 días.");
        t["vision.trend.few"] = ("Log at least two readings to see the trend.", "Registra al menos dos lecturas para ver la tendencia.");
        t["vision.trend.rising"] = ("rising", "subiendo");
        t["vision.trend.falling"] = ("falling", "bajando");
        t["vision.trend.stable"] = ("stable", "estable");
        t["vision.trend"] = ("Trend: {0} ({1} {2} over {3} days)", "Tendencia: {0} ({1} {2} en {3} días)");
        t["vision.corr.few"] = ("Relation with CO₂: not enough days with both vision and CO₂ readings yet ({0} so far).", "Relación con el CO₂: aún no hay suficientes días con lecturas de visión y de CO₂ ({0} por ahora).");
        t["vision.corr.weak"] = ("weak", "débil");
        t["vision.corr.moderate"] = ("moderate", "moderada");
        t["vision.corr.strong"] = ("strong", "fuerte");
        t["vision.corr"] = ("Relation with CO₂: {0} (r = {1}, {2} days). In simulated data a relation does not prove a cause.", "Relación con el CO₂: {0} (r = {1}, {2} días). En datos simulados una relación no prueba una causa.");

        t["vision.sans.heading"] = ("About SANS", "Sobre el SANS");
        t["vision.sans.note"] = ("SANS (Spaceflight-Associated Neuro-ocular Syndrome) is a set of changes in the eyes and vision that some astronauts show after long stays in space. NASA is studying whether the higher CO₂ of a closed cabin may play a part. Here your readings are only compared with the CO₂ of the same period: this is simulated data, a relation is not a cause and nothing here is a diagnosis.", "El SANS (síndrome neuro-ocular asociado a los vuelos espaciales) es un conjunto de cambios en los ojos y la visión que algunos astronautas presentan tras estancias largas en el espacio. La NASA estudia si el CO₂ más alto de una cabina cerrada puede influir. Aquí tus lecturas solo se comparan con el CO₂ del mismo periodo: son datos simulados, una relación no es una causa y nada de esto es un diagnóstico.");
        t["vision.evidence"] = ("Evidence status: {0}", "Estado de la evidencia: {0}");
        t["vision.evidence.verified"] = ("verified", "verificado");
        t["vision.evidence.heuristic"] = ("heuristic (a working rule, not established)", "heurístico (una regla de trabajo, no establecida)");
    }
}
