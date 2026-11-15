namespace Heartbeat.Desktop.Localization;

/// <summary>Textos del modo simple, la introducción y el glosario (HB-FE-01). Sin afirmaciones diagnósticas.</summary>
public static partial class Strings
{
    private static void AddSimple(Dictionary<string, (string En, string Es)> t)
    {
        // Encabezado y etiquetas
        t["simple.heading"] = ("Your status at a glance", "Tu estado de un vistazo");
        t["simple.subtitle"] = ("One light for each of the 5 hazards of space, and one overall.", "Un semáforo por cada uno de los 5 peligros del espacio, y uno global.");
        t["simple.simulated"] = ("Simulated data", "Datos simulados");
        t["simple.simulatedTip"] = ("All data in this app is simulated. It is a decision-support tool, not a medical diagnosis.", "Todos los datos de esta app son simulados. Es una herramienta de apoyo, no un diagnóstico médico.");
        t["simple.overall"] = ("Overall", "Global");
        t["simple.whyLabel"] = ("Why:", "Por qué:");
        t["simple.hazardsAria"] = ("Traffic light for each of the five hazards", "Semáforo de cada uno de los cinco peligros");
        t["simple.detail"] = ("Detail from the rules engine: {0}", "Detalle del motor de reglas: {0}");
        t["simple.source"] = ("Source: {0}", "Fuente: {0}");
        t["simple.evidence"] = ("Value: {0} {1}", "Valor: {0} {1}");
        t["simple.evidence.limit"] = ("Value: {0} {1} · usual limit: {2}", "Valor: {0} {1} · límite habitual: {2}");
        t["simple.showIntro"] = ("Show introduction", "Ver introducción");
        t["simple.glossary"] = ("Glossary", "Glosario");

        // Estado global
        t["simple.headline.normal"] = ("Everything looks in order", "Todo se ve en orden");
        t["simple.headline.watch"] = ("Something to keep an eye on", "Hay algo que vigilar");
        t["simple.headline.critical"] = ("Needs attention now", "Necesita atención ahora");
        t["simple.headline.none"] = ("No data yet", "Aún no hay datos");
        t["simple.global.none"] = ("Log a check-up to see your status.", "Registra un check-up para ver tu estado.");
        t["simple.global.allNormal"] = ("The {0} hazards with data are normal.", "Los {0} peligros con datos están normales.");
        t["simple.global.flagged"] = ("Lighting up: {0}.", "Lo que enciende el semáforo: {0}.");


        // «Por qué» de cada peligro, por estado
        t["simple.why.SpaceRadiation.none"] = ("No radiation doses recorded yet.", "Todavía no hay dosis de radiación registradas.");
        t["simple.why.SpaceRadiation.normal"] = ("Your accumulated dose is well below the career limit.", "Tu dosis acumulada está muy por debajo del límite de carrera.");
        t["simple.why.SpaceRadiation.watch"] = ("Your accumulated dose is getting close to the career limit.", "Tu dosis acumulada se acerca al límite de carrera.");
        t["simple.why.SpaceRadiation.critical"] = ("Your accumulated dose reached the career limit.", "Tu dosis acumulada llegó al límite de carrera.");

        t["simple.why.IsolationAndConfinement.none"] = ("Mission profile not available yet.", "Todavía no hay perfil de misión.");
        t["simple.why.IsolationAndConfinement.normal"] = ("Sleep, mood and isolation indices are in their usual range.", "El sueño, el ánimo y los índices de aislamiento están en su rango habitual.");
        t["simple.why.IsolationAndConfinement.watch"] = ("Sleep, mood or isolation are drifting from their usual range.", "El sueño, el ánimo o el aislamiento se alejan de su rango habitual.");
        t["simple.why.IsolationAndConfinement.critical"] = ("Sleep, mood or isolation are far from their usual range.", "El sueño, el ánimo o el aislamiento están lejos de su rango habitual.");

        t["simple.why.DistanceFromEarth.none"] = ("Mission profile not available yet.", "Todavía no hay perfil de misión.");
        t["simple.why.DistanceFromEarth.normal"] = ("Messages to Earth arrive in a short time.", "Los mensajes a la Tierra llegan en poco tiempo.");
        t["simple.why.DistanceFromEarth.watch"] = ("Messages to Earth take longer: expect slower answers from the ground.", "Los mensajes a la Tierra tardan más: espera respuestas más lentas desde tierra.");
        t["simple.why.DistanceFromEarth.critical"] = ("Messages to Earth take a long time: plan to act with more autonomy.", "Los mensajes a la Tierra tardan mucho: planifica actuar con más autonomía.");

        t["simple.why.GravityFields.none"] = ("Mission profile not available yet.", "Todavía no hay perfil de misión.");
        t["simple.why.GravityFields.normal"] = ("Gravity, blood pressure, heart rate and eye readings are in their usual range.", "La gravedad, la presión arterial, el pulso y las lecturas de los ojos están en su rango habitual.");
        t["simple.why.GravityFields.watch"] = ("Gravity is below Earth's or a related reading is off its usual range: keep up your exercise and check your vitals.", "La gravedad es menor que la terrestre o una lectura relacionada se sale de su rango: mantén el ejercicio y revisa tus signos.");
        t["simple.why.GravityFields.critical"] = ("A gravity-related reading is far from its usual range: tell your crew medical officer.", "Una lectura ligada a la gravedad está lejos de su rango habitual: avisa a tu oficial médico.");

        t["simple.why.HostileClosedEnvironment.none"] = ("No cabin readings saved yet.", "Todavía no hay lecturas de la cabina guardadas.");
        t["simple.why.HostileClosedEnvironment.normal"] = ("Air, pressure, temperature, light and noise in the cabin are in their usual range.", "El aire, la presión, la temperatura, la luz y el ruido de la cabina están en su rango habitual.");
        t["simple.why.HostileClosedEnvironment.watch"] = ("A cabin reading is off its usual range: check the cabin readings below.", "Una lectura de la cabina se sale de su rango habitual: revisa las lecturas de abajo.");
        t["simple.why.HostileClosedEnvironment.critical"] = ("A cabin reading is far from its usual range: tell your crew medical officer.", "Una lectura de la cabina está lejos de su rango habitual: avisa a tu oficial médico.");

        // Introducción de 3 pasos
        t["onb.progress"] = ("Step {0} of {1}", "Paso {0} de {1}");
        t["onb.next"] = ("Next", "Siguiente");
        t["onb.back"] = ("Back", "Atrás");
        t["onb.done"] = ("Got it", "Listo");
        t["onb.skip"] = ("Skip", "Saltar");
        t["onb.step1.title"] = ("Five hazards, five lights", "Cinco peligros, cinco semáforos");
        t["onb.step1.body"] = ("Space life has five hazards: radiation, isolation, distance from Earth, gravity and the closed cabin. Each one has its own traffic light, plus one overall light that shows the worst of them.", "La vida en el espacio tiene cinco peligros: la radiación, el aislamiento, la distancia a la Tierra, la gravedad y la cabina cerrada. Cada uno tiene su semáforo, y hay uno global que muestra el peor de todos.");
        t["onb.step2.title"] = ("What the colors mean", "Qué significan los colores");
        t["onb.step2.body"] = ("Green (Normal): all in its usual range. Yellow (Watch): keep an eye on it. Red (Critical): tell your crew medical officer. Every light also says its status in words and explains why.", "Verde (Normal): todo en su rango habitual. Amarillo (Vigilancia): hay que prestarle atención. Rojo (Crítico): avisa a tu oficial médico. Cada semáforo también dice su estado con palabras y explica por qué.");
        t["onb.step3.title"] = ("Simulated data, not a diagnosis", "Datos simulados, no un diagnóstico");
        t["onb.step3.body"] = ("Everything you see is simulated and meant to support your decisions. It does not diagnose, prevent or treat anything. If a term is unclear, open the glossary.", "Todo lo que ves es simulado y sirve de apoyo a tus decisiones. No diagnostica, previene ni trata nada. Si un término no está claro, abre el glosario.");

        // Glosario corto
        t["gloss.heading"] = ("Glossary", "Glosario");
        t["gloss.hint"] = ("Short meanings of the technical words in this app.", "Significado corto de las palabras técnicas de esta app.");
        t["gloss.msv.term"] = ("mSv (millisievert)", "mSv (milisievert)");
        t["gloss.msv.meaning"] = ("The unit that measures the radiation dose your body receives. It adds up over the whole mission.", "La unidad que mide la dosis de radiación que recibe tu cuerpo. Se va sumando durante toda la misión.");
        t["gloss.co2.term"] = ("CO₂", "CO₂");
        t["gloss.co2.meaning"] = ("Carbon dioxide, the gas we breathe out. In a closed cabin it builds up and has to be removed from the air.", "Dióxido de carbono, el gas que exhalamos. En una cabina cerrada se acumula y hay que quitarlo del aire.");
        t["gloss.sans.term"] = ("SANS", "SANS");
        t["gloss.sans.meaning"] = ("Spaceflight-Associated Neuro-ocular Syndrome: changes in the eyes and vision that some astronauts show after long stays in space.", "Síndrome neuro-ocular asociado a los vuelos espaciales: cambios en los ojos y la visión que algunos astronautas presentan tras estancias largas en el espacio.");
        t["gloss.pvt.term"] = ("PVT", "PVT");
        t["gloss.pvt.meaning"] = ("Psychomotor Vigilance Task: a short reaction-time test that shows how alert you are.", "Prueba de vigilancia psicomotora: un test corto de tiempo de reacción que muestra qué tan alerta estás.");
        t["gloss.iop.term"] = ("IOP", "PIO");
        t["gloss.iop.meaning"] = ("Intraocular pressure: the pressure inside the eye.", "Presión intraocular: la presión dentro del ojo.");
        t["gloss.logmar.term"] = ("logMAR", "logMAR");
        t["gloss.logmar.meaning"] = ("A scale for visual acuity. Lower is better; 0 is normal sharp vision.", "Una escala de agudeza visual. Menos es mejor; 0 es una visión nítida normal.");
        t["gloss.gcr.term"] = ("GCR", "GCR");
        t["gloss.gcr.meaning"] = ("Galactic cosmic rays: a constant background of radiation that comes from outside the solar system.", "Rayos cósmicos galácticos: un fondo constante de radiación que viene de fuera del sistema solar.");
        t["gloss.spe.term"] = ("SPE", "SPE");
        t["gloss.spe.meaning"] = ("Solar particle event: a sudden burst of radiation from the Sun that can add a large dose quickly.", "Evento de partículas solares: una ráfaga repentina de radiación del Sol que puede sumar una dosis grande en poco tiempo.");
        t["gloss.mmhg.term"] = ("mmHg", "mmHg");
        t["gloss.mmhg.meaning"] = ("Millimeters of mercury: the unit used for blood pressure, eye pressure and gas pressure in the cabin.", "Milímetros de mercurio: la unidad de la presión arterial, la presión del ojo y la presión de los gases de la cabina.");
    }
}
