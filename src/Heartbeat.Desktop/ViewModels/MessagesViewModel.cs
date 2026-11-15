using System.Collections.ObjectModel;
using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Heartbeat.Desktop.Converters;
using Heartbeat.Desktop.Info;
using Heartbeat.Desktop.Services;
using Heartbeat.Desktop.Localization;

namespace Heartbeat.Desktop.ViewModels;

/// <summary>
/// Mensajes y reportes con control de misión (MR-11). Se redacta un mensaje o un reporte que adjunta un check-up; sale
/// al sincronizar (en cola mientras no hay enlace). La bandeja reúne las respuestas y las alertas reconocidas que llegan
/// por sync; «Enviados» muestra el estado de cada mensaje: en cola, enviado, leído o rechazado.
/// </summary>
public sealed partial class MessagesViewModel : PageViewModel, IDisposable
{
    private const int MaxCheckUpOptions = 30;

    private readonly IHealthRecordStore _store;
    private readonly ISessionContext _session;
    private readonly IIndicatorInfoProvider _info;
    private string _autoSubject = string.Empty;
    private string _autoBody = string.Empty;

    public MessagesViewModel(IHealthRecordStore store, ISessionContext session, IIndicatorInfoProvider info)
    {
        _store = store;
        _session = session;
        _info = info;
        NewMessage = string.Empty;
        Subject = string.Empty;
        Feedback = string.Empty;
        Error = string.Empty;
        IsInboxTab = true;

        Reload();
        _store.Changed += OnStoreChanged;
    }

    public override string Title => Loc.T("nav.messages");

    /// <summary>Texto del mensaje o del reporte.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string NewMessage { get; set; }

    [ObservableProperty]
    public partial string Subject { get; set; }

    /// <summary><c>true</c> al redactar un reporte (adjunta un check-up); <c>false</c> para un mensaje libre.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyPropertyChangedFor(nameof(IsMessageMode))]
    public partial bool IsReportMode { get; set; }

    public bool IsMessageMode => !IsReportMode;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyPropertyChangedFor(nameof(SelectedAlreadyReported))]
    public partial CheckUpOptionViewModel? SelectedCheckUp { get; set; }

    public bool SelectedAlreadyReported => SelectedCheckUp?.AlreadyReported == true;

    /// <summary>Confirmación tras guardar («Mensaje guardado…»).</summary>
    [ObservableProperty]
    public partial string Feedback { get; set; }

    [ObservableProperty]
    public partial string Error { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSentTab))]
    public partial bool IsInboxTab { get; set; }

    public bool IsSentTab => !IsInboxTab;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InboxTabText))]
    public partial int UnreadCount { get; private set; }

    public ObservableCollection<CheckUpOptionViewModel> CheckUps { get; } = [];

    public ObservableCollection<InboxRowViewModel> Inbox { get; } = [];

    public ObservableCollection<OutgoingRowViewModel> Sent { get; } = [];

    public bool HasCheckUps => CheckUps.Count > 0;

    public bool HasInbox => Inbox.Count > 0;

    public bool HasSent => Sent.Count > 0;

    public string InboxTabText => UnreadCount > 0 ? Loc.T("msg.inbox", UnreadCount) : Loc.T("msg.inboxPlain");

    public void Dispose() => _store.Changed -= OnStoreChanged;

    partial void OnSelectedCheckUpChanged(CheckUpOptionViewModel? value)
    {
        if (value is null)
        {
            return;
        }

        // Se rellena el asunto y el texto con el resumen del check-up, salvo que el usuario ya haya escrito el suyo.
        string subject = ReportBuilder.Subject(value.CheckUp);
        string body = ReportBuilder.Body(value.CheckUp, _info);
        if (string.IsNullOrWhiteSpace(Subject) || Subject == _autoSubject)
        {
            Subject = subject;
        }

        if (string.IsNullOrWhiteSpace(NewMessage) || NewMessage == _autoBody)
        {
            NewMessage = body;
        }

        _autoSubject = subject;
        _autoBody = body;
    }

    partial void OnIsReportModeChanged(bool value)
    {
        Error = string.Empty;
        Feedback = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private void Send()
    {
        // Execute no consulta CanExecute: se protege aquí.
        if (!CanSend() || _session.CurrentUser is not { } user)
        {
            return;
        }

        Error = string.Empty;
        try
        {
            if (IsReportMode)
            {
                CheckUpRecord checkUp = SelectedCheckUp!.CheckUp;
                string subject = string.IsNullOrWhiteSpace(Subject) ? ReportBuilder.Subject(checkUp) : Subject;
                _store.QueueReport(user.Id, checkUp.Id, subject, NewMessage);
                Feedback = Loc.T("msg.feedback.report");
            }
            else
            {
                _store.QueueMessage(user.Id, NewMessage, Subject);
                Feedback = Loc.T("msg.feedback.message");
            }
        }
        catch (ArgumentException ex)
        {
            Error = ex.Message;
            return;
        }

        NewMessage = string.Empty;
        Subject = string.Empty;
        _autoSubject = string.Empty;
        _autoBody = string.Empty;
        SelectedCheckUp = null;
        IsInboxTab = false;
        Reload();
    }

    private bool CanSend() =>
        !string.IsNullOrWhiteSpace(NewMessage) && (!IsReportMode || SelectedCheckUp is { AlreadyReported: false });

    [RelayCommand]
    private void ComposeMessage() => IsReportMode = false;

    [RelayCommand]
    private void ComposeReport() => IsReportMode = true;

    [RelayCommand]
    private void ShowInbox() => IsInboxTab = true;

    [RelayCommand]
    private void ShowSent() => IsInboxTab = false;

    [RelayCommand]
    private void MarkRead(Guid messageId)
    {
        if (_session.CurrentUser is { } user)
        {
            _store.MarkRead(user.Id, messageId);
        }
    }

    [RelayCommand]
    private void MarkAllRead()
    {
        if (_session.CurrentUser is { } user)
        {
            _store.MarkAllRead(user.Id);
        }
    }

    private void OnStoreChanged(object? sender, EventArgs e) => Reload();

    private void Reload()
    {
        Guid? selected = SelectedCheckUp?.CheckUp.Id;
        CheckUps.Clear();
        Inbox.Clear();
        Sent.Clear();

        if (_session.CurrentUser is { } user)
        {
            IReadOnlyList<CheckUpRecord> checkUps = _store.GetCheckUps(user.Id);
            foreach (CheckUpRecord checkUp in checkUps.Take(MaxCheckUpOptions))
            {
                bool reported = _store.HasReport(checkUp.Id);
                string label = Loc.T(
                    "msg.attach.line",
                    checkUp.RecordedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                    StatusPalette.Name(checkUp.OverallStatus),
                    checkUp.Readings.Count) + (reported ? Loc.T("msg.attach.reported") : string.Empty);
                CheckUps.Add(new CheckUpOptionViewModel(checkUp, label, reported));
            }

            foreach (MessageRecord message in _store.GetMessages(user.Id))
            {
                Sent.Add(ToRow(message));
            }

            List<(DateTimeOffset When, InboxRowViewModel Row)> inbox = [];
            foreach (MessageRecord message in _store.GetInbox(user.Id))
            {
                inbox.Add((message.CreatedAt, new InboxRowViewModel(
                    message.Id,
                    string.IsNullOrWhiteSpace(message.Subject) ? Loc.T("msg.fromMc") : message.Subject!,
                    message.Body,
                    message.SenderName ?? Loc.T("mc.name"),
                    Format(message.CreatedAt),
                    false,
                    !message.IsRead)));
            }

            foreach (AlertAcknowledgementRecord alert in _store.GetAcknowledgedAlerts(user.Id))
            {
                DateTimeOffset checkUpTime = checkUps.FirstOrDefault(c => c.Id == alert.CheckUpId)?.RecordedAt ?? alert.AcknowledgedAt;
                string body = string.IsNullOrWhiteSpace(alert.Note)
                    ? Loc.T("msg.alertAck", alert.AcknowledgedBy, Format(checkUpTime))
                    : Loc.T("msg.alertAckNote", alert.AcknowledgedBy, Format(checkUpTime), alert.Note);
                inbox.Add((alert.AcknowledgedAt, new InboxRowViewModel(
                    null, Loc.T("msg.alertTitle"), body, alert.AcknowledgedBy, Format(alert.AcknowledgedAt), true, false)));
            }

            foreach ((DateTimeOffset _, InboxRowViewModel row) in inbox.OrderByDescending(i => i.When))
            {
                Inbox.Add(row);
            }

            UnreadCount = _store.GetUnreadCount(user.Id);
        }
        else
        {
            UnreadCount = 0;
        }

        // Si el check-up elegido sigue en la lista se conserva la selección.
        SelectedCheckUp = selected is null ? null : CheckUps.FirstOrDefault(c => c.CheckUp.Id == selected);

        OnPropertyChanged(nameof(HasCheckUps));
        OnPropertyChanged(nameof(HasInbox));
        OnPropertyChanged(nameof(HasSent));
    }

    private static OutgoingRowViewModel ToRow(MessageRecord message)
    {
        string status = message.Status switch
        {
            MessageStatus.Rejected => Loc.T("msg.status.rejected", message.SyncError),
            MessageStatus.Read => Loc.T("msg.status.read", Format(message.ReadAt ?? message.SentAt ?? message.CreatedAt)),
            MessageStatus.Sent => Loc.T("msg.status.sent", Format(message.SentAt ?? message.CreatedAt)),
            _ => Loc.T("sync.queued"),
        };

        string title = message.IsReport
            ? Loc.T("msg.subject.report", message.Subject)
            : string.IsNullOrWhiteSpace(message.Subject) ? Loc.T("msg.subject.plain") : message.Subject!;
        return new OutgoingRowViewModel(title, message.Body, Format(message.CreatedAt), message.Status, status, message.IsReport);
    }

    private static string Format(DateTimeOffset at) => at.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
}
