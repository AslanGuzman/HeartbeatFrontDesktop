using System.Net.NetworkInformation;

namespace Heartbeat.Desktop.Services;

/// <summary>
/// Detecta el enlace de red del sistema operativo. Solo indica que hay una interfaz activa, no que el servidor
/// responda: el motor de sincronización (ticket #32) lo sustituirá por una comprobación contra la API.
/// </summary>
public sealed class NetworkConnectivityService : IConnectivityService, IDisposable
{
    private readonly object _gate = new();
    private bool _isOnline;

    public NetworkConnectivityService()
    {
        _isOnline = NetworkInterface.GetIsNetworkAvailable();
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
    }

    public bool IsOnline
    {
        get
        {
            lock (_gate)
            {
                return _isOnline;
            }
        }
    }

    public event EventHandler? IsOnlineChanged;

    public void Dispose()
    {
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
    }

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        bool current = NetworkInterface.GetIsNetworkAvailable();

        lock (_gate)
        {
            if (current == _isOnline)
            {
                return;
            }

            _isOnline = current;
        }

        IsOnlineChanged?.Invoke(this, EventArgs.Empty);
    }
}
