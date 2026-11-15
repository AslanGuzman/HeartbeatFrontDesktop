using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Heartbeat.Desktop.Services;
using Heartbeat.Shared;
using Heartbeat.Desktop.Localization;

namespace Heartbeat.Desktop.ViewModels;

/// <summary>Una dosis de radiación de la lista.</summary>
public sealed record RadiationRowViewModel(string DoseText, string DateText, string? Note, string SyncText, bool IsSynced)
{
    public bool HasNote => !string.IsNullOrEmpty(Note);
}

public sealed record RadiationTimelineRowViewModel(string DateText, string DoseText, string RateText, string TypeText);

/// <summary>
/// Registro de exposición a radiación (NR-07): el astronauta anota dosis en mSv y ve su total acumulado en la misión.
/// Las dosis se guardan en local y se sincronizan como el resto de datos.
/// </summary>
public sealed partial class RadiationViewModel : PageViewModel, IDisposable
{
    private readonly IHealthRecordStore _store;
    private readonly ISessionContext _session;
    private readonly IUrlLauncher _launcher;
    private readonly RadiationDemoCatalog _catalog = RadiationDemoCatalog.Load();

    public RadiationViewModel(IHealthRecordStore store, ISessionContext session, IUrlLauncher launcher)
    {
        _store = store;
        _session = session;
        _launcher = launcher;
        DoseText = string.Empty;
        NoteText = string.Empty;
        Error = string.Empty;
        TotalText = FormatMsv(0);
        TimelineRows = new ObservableCollection<RadiationTimelineRowViewModel>();
        ShelterActionText = string.Empty;

        Reload();
        _store.Changed += OnStoreChanged;
        Loc.Manager.LanguageChanged += OnLanguageChanged;
    }

    public override string Title => Loc.T("nav.radiation");

    [ObservableProperty]
    public partial string DoseText { get; set; }

    [ObservableProperty]
    public partial string NoteText { get; set; }

    [ObservableProperty]
    public partial string Error { get; set; }

    [ObservableProperty]
    public partial string TotalText { get; set; }

    [ObservableProperty]
    public partial double LimitProgress { get; set; }

    [ObservableProperty]
    public partial string LimitText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PercentText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LimitStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ShelterActionText { get; set; }

    public ObservableCollection<RadiationRowViewModel> Doses { get; } = [];

    public ObservableCollection<RadiationTimelineRowViewModel> TimelineRows { get; }

    public bool HasItems => Doses.Count > 0;

    public bool HasSpeAlert => _catalog.SpeAlert.Active;

    public int NoteMaxLength => RadiationDoses.NoteMaxLength;

    public string SourceTitle => Loc.T("rad.sourceTitle");

    public Uri SourceUrl => new(_catalog.LimitSource);

    public void Dispose()
    {
        _store.Changed -= OnStoreChanged;
        Loc.Manager.LanguageChanged -= OnLanguageChanged;
    }

    [RelayCommand]
    private void Add()
    {
        if (_session.CurrentUser is not { } user)
        {
            return;
        }

        if (!TryParseDose(DoseText, out decimal dose))
        {
            Error = Loc.T("rad.err.dose");
            return;
        }

        if (dose <= 0)
        {
            Error = Loc.T("rad.err.positive");
            return;
        }

        if (dose > RadiationDoses.MaxEntryMsv)
        {
            Error = Loc.T("rad.err.max", RadiationDoses.MaxEntryMsv);
            return;
        }

        try
        {
            _store.AddRadiationDose(user.Id, dose, NoteText);
        }
        catch (ArgumentException ex)
        {
            Error = ex.Message;
            return;
        }

        DoseText = string.Empty;
        NoteText = string.Empty;
        Error = string.Empty;
        Reload();
    }

    [RelayCommand]
    private void OpenSource()
    {
        if (!_launcher.TryOpen(SourceUrl))
        {
            Error = Loc.T("rad.err.link");
        }
    }

    [RelayCommand]
    private void GoToShelter() => ShelterActionText = Loc.T("rad.shelterSimulated");

    // Acepta punto o coma decimal, sin separador de miles.
    internal static bool TryParseDose(string? text, out decimal dose)
    {
        string normalized = (text ?? string.Empty).Trim().Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out dose);
    }

    private static string FormatMsv(decimal value) => string.Create(CultureInfo.CurrentCulture, $"{value:0.###} mSv");

    private void OnStoreChanged(object? sender, EventArgs e) => Reload();

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Reload();
        OnPropertyChanged(nameof(SourceTitle));
    }

    private void Reload()
    {
        Doses.Clear();
        decimal total = 0;

        if (_session.CurrentUser is { } user)
        {
            total = _store.GetRadiationTotal(user.Id);
            foreach (RadiationDoseRecord dose in _store.GetRadiationDoses(user.Id))
            {
                Doses.Add(new RadiationRowViewModel(
                    FormatMsv(dose.DoseMsv),
                    dose.RecordedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                    dose.Note,
                    dose.SyncedAt is { } synced
                        ? Loc.T("rad.sentAt", synced.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
                        : Loc.T("sync.queued"),
                    dose.IsSynced));
            }
        }

        TotalText = FormatMsv(total);
        decimal percent = _catalog.PercentUsed(total);
        LimitProgress = (double)Math.Clamp(percent, 0, 100);
        LimitText = FormatMsv(_catalog.LimitMsv);
        PercentText = Loc.T("rad.percentUsed", percent);
        LimitStatusText = Loc.T("rad.catalogStatus", _catalog.LimitStatus);
        TimelineRows.Clear();
        foreach (RadiationDemoTimelineEntry entry in _catalog.Timeline)
        {
            TimelineRows.Add(new RadiationTimelineRowViewModel(
                Loc.T("rad.daysAgo", entry.DaysAgo),
                FormatMsv(entry.DoseMsv),
                Loc.T("rad.rate", entry.RateMsvPerDay),
                Loc.T($"rad.type.{entry.Type}")));
        }

        ShelterActionText = string.Empty;
        OnPropertyChanged(nameof(HasItems));
    }

    private sealed record RadiationDemoCatalog(
        decimal LimitMsv,
        string LimitStatus,
        string LimitSource,
        string PercentFormula,
        string TimelineStatus,
        string TimelineSource,
        IReadOnlyList<RadiationDemoTimelineEntry> Timeline,
        RadiationDemoSpeAlert SpeAlert)
    {
        public decimal PercentUsed(decimal totalMsv) => PercentFormula switch
        {
            "totalMsv / limitMsv * 100" => totalMsv / LimitMsv * 100m,
            _ => throw new InvalidOperationException("La fórmula de porcentaje de radiación no está soportada."),
        };

        public static RadiationDemoCatalog Load()
        {
            using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Heartbeat.Desktop.RadiationDemoCatalog.json")
                ?? throw new InvalidOperationException("Falta el catálogo de radiación simulado.");
            return JsonSerializer.Deserialize<RadiationDemoCatalog>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("El catálogo de radiación simulado no es válido.");
        }
    }

    private sealed record RadiationDemoTimelineEntry(int DaysAgo, decimal DoseMsv, decimal RateMsvPerDay, string Type);

    private sealed record RadiationDemoSpeAlert(bool Active, string Status, string Source);
}
