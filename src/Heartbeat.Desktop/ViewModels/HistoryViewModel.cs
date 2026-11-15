using System.Collections.ObjectModel;
using System.Globalization;

using Heartbeat.Desktop.Converters;
using Heartbeat.Desktop.Info;
using Heartbeat.Desktop.Services;

namespace Heartbeat.Desktop.ViewModels;

/// <summary>Historial de check-ups del astronauta con la sesión abierta (MR-13, MR-14).</summary>
public sealed class HistoryViewModel : PageViewModel, IDisposable
{
    private readonly IHealthRecordStore _store;
    private readonly ISessionContext _session;
    private readonly IIndicatorInfoProvider _info;

    public HistoryViewModel(IHealthRecordStore store, ISessionContext session, IIndicatorInfoProvider info)
    {
        _store = store;
        _session = session;
        _info = info;

        Reload();
        _store.Changed += OnStoreChanged;
    }

    public override string Title => "Historial";

    public ObservableCollection<CheckUpRowViewModel> CheckUps { get; } = [];

    public bool HasItems => CheckUps.Count > 0;

    public void Dispose() => _store.Changed -= OnStoreChanged;

    private void OnStoreChanged(object? sender, EventArgs e) => Reload();

    private void Reload()
    {
        CheckUps.Clear();
        if (_session.CurrentUser is { } user)
        {
            foreach (CheckUpRecord checkUp in _store.GetCheckUps(user.Id))
            {
                CheckUps.Add(ToRow(checkUp));
            }
        }

        OnPropertyChanged(nameof(HasItems));
    }

    private CheckUpRowViewModel ToRow(CheckUpRecord checkUp) => new(
        checkUp.RecordedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
        checkUp.OverallStatus,
        StatusPalette.Name(checkUp.OverallStatus),
        checkUp.IsRejected ? $"Rechazado: {checkUp.SyncError}" : checkUp.IsSynced ? "Sincronizado" : "Pendiente de sincronizar",
        checkUp.IsSynced,
        checkUp.Readings
            .Select(r => new ReadingRowViewModel(
                _info.Find(IndicatorPresentation.IdOf(r.Input.Indicator))?.Name ?? r.Input.Indicator.ToString(),
                IndicatorPresentation.FormatValue(r.Input),
                r.Evaluation.Status,
                StatusPalette.Name(r.Evaluation.Status),
                IndicatorPresentation.GuidanceOf(r.Evaluation)))
            .ToList());
}
