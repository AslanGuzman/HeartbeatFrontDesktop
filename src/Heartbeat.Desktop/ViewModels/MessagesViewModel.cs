using System.Collections.ObjectModel;
using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Heartbeat.Desktop.Services;

namespace Heartbeat.Desktop.ViewModels;

/// <summary>
/// Mensajes a control de misión (MR-11): se encolan en local y salen al recuperar el enlace; con enlace salen de
/// inmediato.
/// </summary>
public sealed partial class MessagesViewModel : PageViewModel, IDisposable
{
    private readonly IHealthRecordStore _store;
    private readonly ISessionContext _session;

    public MessagesViewModel(IHealthRecordStore store, ISessionContext session)
    {
        _store = store;
        _session = session;
        NewMessage = string.Empty;

        Reload();
        _store.Changed += OnStoreChanged;
    }

    public override string Title => "Mensajes";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(QueueCommand))]
    public partial string NewMessage { get; set; }

    public ObservableCollection<MessageRowViewModel> Messages { get; } = [];

    public bool HasItems => Messages.Count > 0;

    public void Dispose() => _store.Changed -= OnStoreChanged;

    [RelayCommand(CanExecute = nameof(CanQueue))]
    private void Queue()
    {
        if (_session.CurrentUser is not { } user)
        {
            return;
        }

        _store.QueueMessage(user.UserName, NewMessage);
        NewMessage = string.Empty;
        Reload();
    }

    private bool CanQueue() => !string.IsNullOrWhiteSpace(NewMessage);

    private void OnStoreChanged(object? sender, EventArgs e) => Reload();

    private void Reload()
    {
        Messages.Clear();
        if (_session.CurrentUser is { } user)
        {
            foreach (MessageRecord message in _store.GetMessages(user.UserName))
            {
                Messages.Add(new MessageRowViewModel(
                    message.Body,
                    message.CreatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                    message.SentAt is { } sent
                        ? string.Create(CultureInfo.CurrentCulture, $"Enviado {sent.ToLocalTime():g}")
                        : "En cola (sin enlace)",
                    message.IsSent));
            }
        }

        OnPropertyChanged(nameof(HasItems));
    }
}
