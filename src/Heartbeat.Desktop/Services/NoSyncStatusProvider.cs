namespace Heartbeat.Desktop.Services;

/// <summary>Implementación provisional: aún no hay base local ni motor de sincronización, así que nunca hay nada sincronizado ni pendiente.</summary>
public sealed class NoSyncStatusProvider : ISyncStatusProvider
{
    public DateTimeOffset? LastSyncAt => null;

    public int PendingCount => 0;

    // Nunca cambia; los accesores vacíos evitan el aviso CS0067 de evento sin usar.
    public event EventHandler? Changed
    {
        add { }
        remove { }
    }
}
