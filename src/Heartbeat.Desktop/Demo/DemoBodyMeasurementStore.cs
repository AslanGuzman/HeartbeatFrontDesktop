using Heartbeat.Desktop.Services;

namespace Heartbeat.Desktop.Demo;

/// <summary>Medidas corporales en memoria, con un historial de ejemplo para los astronautas de demostración.</summary>
public sealed class DemoBodyMeasurementStore : IBodyMeasurementStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, List<BodyMeasurement>> _byUser = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _clock;

    public DemoBodyMeasurementStore(TimeProvider clock)
    {
        _clock = clock;
        DateTimeOffset now = clock.GetUtcNow();

        // Tendencia típica en órbita: altura +, cuello +, pecho y cintura -, muslo y pantorrilla - (pérdida de volumen en piernas).
        Seed("ana", now.AddDays(-14), 168.0m, 32.0m, 88.0m, 70.0m, 54.0m, 36.0m);
        Seed("ana", now.AddDays(-7), 168.6m, 32.4m, 87.5m, 69.0m, 53.0m, 35.5m);
        Seed("luis", now.AddDays(-10), 180.0m, 38.0m, 100.0m, 86.0m, 58.0m, 38.0m);
        Seed("mara", now.AddDays(-5), 172.0m, 33.5m, 90.0m, 72.0m, 55.0m, 36.5m);
    }

    public IReadOnlyList<BodyMeasurement> GetHistory(string userName, BodyRegion region)
    {
        lock (_gate)
        {
            return _byUser.TryGetValue(userName ?? string.Empty, out List<BodyMeasurement>? list)
                ? list.Where(m => m.Region == region).OrderByDescending(m => m.RecordedAt).ToList()
                : [];
        }
    }

    public void Add(string userName, IReadOnlyList<(BodyRegion Region, decimal Centimeters)> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentNullException.ThrowIfNull(values);

        DateTimeOffset now = _clock.GetUtcNow();
        lock (_gate)
        {
            if (!_byUser.TryGetValue(userName, out List<BodyMeasurement>? list))
            {
                _byUser[userName] = list = [];
            }

            list.AddRange(values.Select(v => new BodyMeasurement(v.Region, v.Centimeters, now)));
        }
    }

    private void Seed(string user, DateTimeOffset at, decimal height, decimal neck, decimal chest, decimal waist, decimal thigh, decimal calf)
    {
        Add(user, [(BodyRegion.Height, height), (BodyRegion.Neck, neck), (BodyRegion.Chest, chest),
                   (BodyRegion.Waist, waist), (BodyRegion.Thigh, thigh), (BodyRegion.Calf, calf)], at);
    }

    private void Add(string user, IReadOnlyList<(BodyRegion Region, decimal Centimeters)> values, DateTimeOffset at)
    {
        if (!_byUser.TryGetValue(user, out List<BodyMeasurement>? list))
        {
            _byUser[user] = list = [];
        }

        list.AddRange(values.Select(v => new BodyMeasurement(v.Region, v.Centimeters, at)));
    }
}
