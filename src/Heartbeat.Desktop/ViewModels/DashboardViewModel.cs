using System.Globalization;

using CommunityToolkit.Mvvm.Input;

using Heartbeat.Desktop.Converters;
using Heartbeat.Desktop.Info;
using Heartbeat.Desktop.Navigation;
using Heartbeat.Desktop.Services;
using Heartbeat.Desktop.Services.Profile;
using Heartbeat.Shared;
using Heartbeat.Desktop.Localization;

namespace Heartbeat.Desktop.ViewModels;

/// <summary>
/// Panel de salud: el estado actual de los seis indicadores en una sola pantalla (MR-05). Arriba, el estado general;
/// una tarjeta por indicador con su último valor, unidad, estado en color y texto (NR-01) y hora; y el botón para
/// registrar un nuevo check-up.
/// </summary>
public sealed partial class DashboardViewModel : PageViewModel
{
    private readonly IIndicatorInfoPresenter _infoPresenter;
    private readonly INavigationService _navigation;
    private readonly CheckUpReminder _reminder;
    private readonly IProfileStore _profiles;

    private static readonly string[] EnvironmentIndicatorIds =
        ["gravity", "co2", "o2", "pressure", "temperature", "humidity", "light", "noise"];

    public DashboardViewModel(
        IHealthRecordStore store,
        ISessionContext session,
        IIndicatorInfoProvider info,
        IIndicatorInfoPresenter infoPresenter,
        INavigationService navigation,
        TimeProvider clock,
        CheckUpReminder reminder,
        IProfileStore profiles,
        IEnvironmentReadingStore environment)
    {
        _infoPresenter = infoPresenter;
        _navigation = navigation;
        _reminder = reminder;
        _profiles = profiles;

        SessionUser? user = session.CurrentUser;
        IReadOnlyList<CheckUpRecord> checkUps = user is null ? [] : store.GetCheckUps(user.Id);
        ProfileView? profile = user is null ? null : _profiles.Get(user.Id);

        // Un check-up puede omitir indicadores (1 a 6 lecturas): cada tarjeta muestra la lectura más reciente de su indicador.
        List<IndicatorCardViewModel> cards = [];
        foreach (IndicatorType type in Enum.GetValues<IndicatorType>())
        {
            CheckUpRecord? source = checkUps.FirstOrDefault(c => c.Readings.Any(r => r.Input.Indicator == type));
            ReadingRecord? reading = source?.Readings.First(r => r.Input.Indicator == type);
            cards.Add(BuildCard(type, source, reading, info, clock));
        }

        Cards = cards;
        Regions = BuildRegions(cards, info);
        EnvironmentCards = BuildEnvironmentCards(
            profile?.Profile.Mission.EnvironmentProfile,
            user is null ? [] : environment.GetLatest(user.Id),
            clock);
        List<HealthStatus> environmentStatuses = EnvironmentCards
            .Where(c => c.HasData && c.Status is not null)
            .Select(c => c.Status!.Value)
            .ToList();
        EnvironmentStatus = environmentStatuses.Count == 0 ? null : environmentStatuses.Max();
        EnvironmentStatusText = EnvironmentStatus is { } environmentStatus
            ? Loc.T("status.overall", StatusPalette.Name(environmentStatus))
            : Loc.T("env.noData");
        EnvironmentProfileText = BuildEnvironmentProfileText(profile?.Profile.Mission.EnvironmentProfile);
        Greeting = user is null ? Loc.T("dash.greeting") : Loc.T("dash.greetingName", user.FirstName);
        DateText = clock.GetLocalNow().ToString(Loc.T("dash.dateFormat"), Loc.Culture);

        List<IndicatorCardViewModel> withData = cards.Where(c => c.HasData).ToList();
        HasData = withData.Count > 0;
        OverallStatus = HasData ? withData.Max(c => c.Status) : null;
        OverallText = OverallStatus is { } overall
            ? Loc.T("status.overall", StatusPalette.Name(overall))
            : Loc.T("dash.noMeasurements");

        int watch = withData.Count(c => c.Status == HealthStatus.Watch);
        int critical = withData.Count(c => c.Status == HealthStatus.Critical);
        SummaryText = BuildSummary(withData.Count, watch, critical, checkUps.FirstOrDefault(), clock);

        CheckUpReminderSettings reminderSettings = _reminder.Settings;
        IsCheckUpPending = _reminder.IsPending;
        PendingCheckUpText = reminderSettings.Enabled
            ? $"Aún no registras tu check-up de hoy. Te avisaremos a las {reminderSettings.Time.ToString("HH:mm", CultureInfo.InvariantCulture)}."
            : "Aún no registras tu check-up de hoy. Activa el recordatorio en tu perfil.";

        CriticalNames = string.Join(", ", withData.Where(c => c.Status == HealthStatus.Critical).Select(c => c.Name));
        HasCritical = critical > 0;
    }

    public override string Title => Loc.T("nav.dashboard");

    public string Greeting { get; }

    public string DateText { get; }

    public bool HasData { get; }

    /// <summary>Peor estado entre las últimas lecturas de cada indicador; <c>null</c> si aún no hay ninguna.</summary>
    public HealthStatus? OverallStatus { get; }

    public string OverallText { get; }

    /// <summary>Qué requiere atención y cuándo fue el último check-up, por ejemplo «1 en vigilancia · Último check-up: Hoy, 19:45».</summary>
    public string SummaryText { get; }

    /// <summary>Hoy todavía no hay check-up: se muestra el aviso para registrarlo (P2-REMIND).</summary>
    public bool IsCheckUpPending { get; }

    /// <summary>Explica cuándo llegará el recordatorio o cómo activarlo.</summary>
    public string PendingCheckUpText { get; }

    /// <summary>Hay al menos un indicador crítico: se destaca el aviso de contactar al oficial médico (MR-06).</summary>
    public bool HasCritical { get; }

    public string CriticalNames { get; }

    public IReadOnlyList<IndicatorCardViewModel> Cards { get; }

    /// <summary>Regiones de la figura humana con el peor estado de sus indicadores (NR-04).</summary>
    public IReadOnlyList<BodyRegionViewModel> Regions { get; }

    public IReadOnlyList<EnvironmentCardViewModel> EnvironmentCards { get; }

    public HealthStatus? EnvironmentStatus { get; }

    public string EnvironmentStatusText { get; }

    public string EnvironmentProfileText { get; }

    /// <summary>
    /// Clic en una región: abre la información de su indicador más grave (o el primero si aún no hay mediciones).
    /// </summary>
    [RelayCommand]
    private void ShowRegion(string? regionId)
    {
        BodyRegionViewModel? region = Regions.FirstOrDefault(r => r.Id == regionId);
        if (region is not null)
        {
            _infoPresenter.Show(region.InfoIndicatorId);
        }
    }

    // Trazos de las regiones sobre la figura de 120 × 330 (cabeza, pecho y abdomen).
    private static readonly (string Id, string Name, string Geometry)[] RegionShapes =
    [
        ("head", "region.head", "M60,0 C72,0 80,12 80,28 C80,44 71,56 60,56 C49,56 40,44 40,28 C40,12 48,0 60,0 Z"),
        ("heart", "region.heart", "M40,66 L80,66 C90,68 96,76 98,92 L98,124 L22,124 L22,92 C24,76 30,68 40,66 Z"),
        ("abdomen", "region.abdomen", "M22,128 L98,128 L96,176 C90,186 30,186 24,176 Z"),
    ];

    private static List<BodyRegionViewModel> BuildRegions(List<IndicatorCardViewModel> cards, IIndicatorInfoProvider info)
    {
        List<BodyRegionViewModel> regions = [];
        foreach ((string id, string nameKey, string geometry) in RegionShapes)
        {
            string name = Loc.T(nameKey);
            List<IndicatorCardViewModel> members = info.Indicators
                .Where(i => i.BodyRegion == id)
                .Select(i => cards.First(c => c.Id == i.Id))
                .ToList();
            if (members.Count == 0)
            {
                continue;
            }

            List<IndicatorCardViewModel> measured = members.Where(c => c.HasData).ToList();
            HealthStatus? worst = measured.Count == 0 ? null : measured.Max(c => c.Status);

            // El más grave; a igualdad, el primero del contrato.
            IndicatorCardViewModel focus = worst is null ? members[0] : measured.First(c => c.Status == worst);
            string summary = worst is null
                ? Loc.T("region.noData", name)
                : $"{name}: {StatusPalette.Name(worst)} · {focus.Name}";

            regions.Add(new BodyRegionViewModel(
                id,
                name,
                geometry,
                worst,
                StatusPalette.Name(worst),
                summary,
                focus.Id,
                members.Select(c => new RegionIndicatorViewModel(
                    c.Id, c.Name, c.HasData ? c.Status : null, c.StatusText, c.HasData ? $"{c.ValueText} {c.UnitText}" : "—")).ToList()));
        }

        return regions;
    }

    private static List<EnvironmentCardViewModel> BuildEnvironmentCards(
        EnvironmentProfile? profile,
        IReadOnlyList<EnvironmentReadingRecord> readings,
        TimeProvider clock)
    {
        Dictionary<string, EnvironmentReadingRecord> latest = readings.ToDictionary(
            r => r.IndicatorId,
            StringComparer.OrdinalIgnoreCase);
        List<EnvironmentCardViewModel> cards = [];

        foreach (string id in EnvironmentIndicatorIds)
        {
            IndicatorCatalogEntry? entry = IndicatorsCatalog.Find(id);
            if (entry is null)
            {
                continue;
            }

            if (id == "gravity" && profile is { } environmentProfile)
            {
                cards.Add(BuildEnvironmentCard(entry, environmentProfile.GravityG, Loc.T("env.profile")));
            }
            else if (latest.TryGetValue(id, out EnvironmentReadingRecord? reading))
            {
                cards.Add(BuildEnvironmentCard(
                    entry,
                    reading.Value,
                    IndicatorPresentation.FormatWhen(reading.RecordedAt, clock)));
            }
            else
            {
                cards.Add(new EnvironmentCardViewModel(
                    entry.Id,
                    EnvironmentPresentation.GlyphOf(entry.Id),
                    EnvironmentPresentation.NameOf(entry.Id),
                    null,
                    StatusPalette.Name(null),
                    "—",
                    entry.Unit,
                    Loc.T("env.card.none"),
                    false));
            }
        }

        return cards;
    }

    private static EnvironmentCardViewModel BuildEnvironmentCard(
        IndicatorCatalogEntry entry,
        decimal value,
        string takenAtText)
    {
        IndicatorBand band = IndicatorsCatalog.Evaluate(entry, value);
        return new EnvironmentCardViewModel(
            entry.Id,
            EnvironmentPresentation.GlyphOf(entry.Id),
            EnvironmentPresentation.NameOf(entry.Id),
            band.Status,
            StatusPalette.Name(band.Status),
            value.ToString("0.##", Loc.Culture),
            entry.Unit,
            takenAtText,
            true);
    }

    private static string BuildEnvironmentProfileText(EnvironmentProfile? profile)
    {
        if (profile is null)
        {
            return Loc.T("env.noProfile");
        }

        string type = profile.Type switch
        {
            "station" => Loc.T("profile.env.station"),
            "moon" or "lunarBase" => Loc.T("profile.env.moon"),
            "mars" or "marsBase" => Loc.T("profile.env.mars"),
            _ => profile.Type,
        };
        return Loc.T(
            "env.profileDetails",
            type,
            profile.CrewSize,
            profile.DistanceKm.ToString("0.##", Loc.Culture),
            profile.HabitableVolumeM3.ToString("0.##", Loc.Culture));
    }

    /// <summary>Abre el formulario para registrar un nuevo check-up.</summary>
    [RelayCommand]
    private void NewCheckUp() => _navigation.NavigateTo<CheckUpViewModel>();

    /// <summary>Abre el panel de información; con un id lo deja directamente en ese indicador.</summary>
    [RelayCommand]
    private void ShowInfo(string? indicatorId) => _infoPresenter.Show(indicatorId);

    private static string BuildSummary(int measured, int watch, int critical, CheckUpRecord? latest, TimeProvider clock)
    {
        if (latest is null)
        {
            return Loc.T("dash.firstCheckUp");
        }

        List<string> parts = [];
        if (critical > 0)
        {
            parts.Add(critical == 1 ? Loc.T("dash.critical.one") : Loc.T("dash.critical.many", critical));
        }

        if (watch > 0)
        {
            parts.Add(watch == 1 ? Loc.T("dash.watch.one") : Loc.T("dash.watch.many", watch));
        }

        string attention = parts.Count > 0
            ? string.Join(", ", parts)
            : measured == 6 ? Loc.T("dash.allInRange") : Loc.T("dash.someInRange", measured);

        return Loc.T("dash.lastCheckUp", attention, IndicatorPresentation.FormatWhen(latest.RecordedAt, clock));
    }

    private static IndicatorCardViewModel BuildCard(IndicatorType type, CheckUpRecord? source, ReadingRecord? reading, IIndicatorInfoProvider info, TimeProvider clock)
    {
        string id = IndicatorPresentation.IdOf(type);
        string name = info.Find(id)?.Name ?? id;
        string glyph = IndicatorPresentation.GlyphOf(type);
        string unit = IndicatorPresentation.UnitOf(type);

        if (source is null || reading is null)
        {
            return new IndicatorCardViewModel(
                id, glyph, name, null, StatusPalette.Name(null), "—", unit, Loc.T("dash.card.none"),
                Loc.T("dash.card.hint"), false);
        }

        return new IndicatorCardViewModel(
            id,
            glyph,
            name,
            reading.Evaluation.Status,
            StatusPalette.Name(reading.Evaluation.Status),
            IndicatorPresentation.FormatNumber(reading.Input),
            unit,
            IndicatorPresentation.FormatWhen(source.RecordedAt, clock),
            IndicatorPresentation.GuidanceOf(reading.Evaluation),
            true);
    }
}
