using System.Collections.ObjectModel;
using System.Globalization;

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

/// <summary>
/// Registro de exposición a radiación (NR-07): el astronauta anota dosis en mSv y ve su total acumulado en la misión.
/// Las dosis se guardan en local y se sincronizan como el resto de datos.
/// </summary>
public sealed partial class RadiationViewModel : PageViewModel, IDisposable
{
    private readonly IHealthRecordStore _store;
    private readonly ISessionContext _session;
    private readonly IUrlLauncher _launcher;
    private readonly SourceLink _source = ReferenceRanges.GetSource(RadiationDoses.SourceId);

    public RadiationViewModel(IHealthRecordStore store, ISessionContext session, IUrlLauncher launcher)
    {
        _store = store;
        _session = session;
        _launcher = launcher;
        DoseText = string.Empty;
        NoteText = string.Empty;
        Error = string.Empty;
        TotalText = FormatMsv(0);

        Reload();
        _store.Changed += OnStoreChanged;
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

    public ObservableCollection<RadiationRowViewModel> Doses { get; } = [];

    public bool HasItems => Doses.Count > 0;

    public int NoteMaxLength => RadiationDoses.NoteMaxLength;

    public string SourceTitle => _source.Title;

    public Uri SourceUrl => _source.Url;

    public void Dispose() => _store.Changed -= OnStoreChanged;

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
        if (!_launcher.TryOpen(_source.Url))
        {
            Error = Loc.T("rad.err.link");
        }
    }

    // Acepta punto o coma decimal, sin separador de miles.
    internal static bool TryParseDose(string? text, out decimal dose)
    {
        string normalized = (text ?? string.Empty).Trim().Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out dose);
    }

    private static string FormatMsv(decimal value) => string.Create(CultureInfo.CurrentCulture, $"{value:0.###} mSv");

    private void OnStoreChanged(object? sender, EventArgs e) => Reload();

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
        OnPropertyChanged(nameof(HasItems));
    }
}
