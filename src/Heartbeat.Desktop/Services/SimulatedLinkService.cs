namespace Heartbeat.Desktop.Services;

/// <summary>Está en línea solo si hay red real <b>y</b> el enlace simulado está encendido.</summary>
public sealed class SimulatedLinkService : IConnectivityService, ISimulatedLink, IDisposable
{
    private readonly NetworkConnectivityService _network;
    private readonly object _gate = new();
    private bool _isLinkEnabled = true;

    public SimulatedLinkService(NetworkConnectivityService network)
    {
        _network = network;
        _network.IsOnlineChanged += OnNetworkChanged;
    }

    public bool IsOnline
    {
        get
        {
            lock (_gate)
            {
                return _network.IsOnline && _isLinkEnabled;
            }
        }
    }

    public bool IsLinkEnabled
    {
        get
        {
            lock (_gate)
            {
                return _isLinkEnabled;
            }
        }
        set
        {
            bool before;
            bool after;
            lock (_gate)
            {
                if (_isLinkEnabled == value)
                {
                    return;
                }

                before = _network.IsOnline && _isLinkEnabled;
                _isLinkEnabled = value;
                after = _network.IsOnline && _isLinkEnabled;
            }

            if (before != after)
            {
                IsOnlineChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public event EventHandler? IsOnlineChanged;

    public void Dispose() => _network.IsOnlineChanged -= OnNetworkChanged;

    // La red real solo importa si el enlace simulado está encendido; si no, seguimos sin conexión.
    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        if (IsLinkEnabled)
        {
            IsOnlineChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
