using Heartbeat.Desktop.Localization;

namespace Heartbeat.Desktop.ViewModels;

internal static class EnvironmentPresentation
{
    public static string GlyphOf(string id) => id switch
    {
        "gravity" => "G",
        "co2" => "C",
        "o2" => "O",
        "pressure" => "P",
        "temperature" => "T",
        "humidity" => "H",
        "light" => "L",
        "noise" => "N",
        _ => "•",
    };

    public static string NameOf(string id) => id switch
    {
        "gravity" => Loc.T("env.gravity"),
        "co2" => Loc.T("env.co2"),
        "o2" => Loc.T("env.o2"),
        "pressure" => Loc.T("env.pressure"),
        "temperature" => Loc.T("env.temperature"),
        "humidity" => Loc.T("env.humidity"),
        "light" => Loc.T("env.light"),
        "noise" => Loc.T("env.noise"),
        _ => id,
    };
}
