using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Heartbeat.Desktop.Services;

namespace Heartbeat.Desktop.ViewModels;

/// <summary>Una medida corporal en el panel: valor actual, cambio frente a la anterior y campo para registrar una nueva.</summary>
public sealed partial class BodyMetricViewModel : ObservableObject
{
    public BodyMetricViewModel(BodyRegion region, string name, decimal min, decimal max, double ringTop, double ringLeft, double ringWidth)
    {
        Region = region;
        Name = name;
        Min = min;
        Max = max;
        RingTop = ringTop;
        RingLeft = ringLeft;
        RingWidth = ringWidth;
        Input = string.Empty;
        CurrentText = "—";
        DeltaText = string.Empty;
    }

    public BodyRegion Region { get; }

    public string Name { get; }

    /// <summary>Límites de lo que se puede medir, en cm.</summary>
    public decimal Min { get; }

    public decimal Max { get; }

    // Posición del aro sobre la figura (coordenadas del lienzo del panel).
    public double RingTop { get; }

    public double RingLeft { get; }

    public double RingWidth { get; }

    [ObservableProperty]
    public partial string Input { get; set; }

    [ObservableProperty]
    public partial string CurrentText { get; private set; }

    /// <summary>Cambio frente a la medida anterior, por ejemplo «▲ +0,6 cm»; vacío si no hay con qué comparar.</summary>
    [ObservableProperty]
    public partial string DeltaText { get; private set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Recalcula el valor actual y el cambio a partir del historial (más reciente primero).</summary>
    public void Refresh(IReadOnlyList<BodyMeasurement> history)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        if (history.Count == 0)
        {
            CurrentText = "—";
            DeltaText = string.Empty;
            return;
        }

        CurrentText = string.Create(culture, $"{history[0].Centimeters:0.0} cm");
        if (history.Count < 2)
        {
            DeltaText = string.Empty;
            return;
        }

        decimal delta = history[0].Centimeters - history[1].Centimeters;
        DeltaText = delta switch
        {
            > 0 => string.Create(culture, $"▲ +{delta:0.0} cm"),
            < 0 => string.Create(culture, $"▼ {delta:0.0} cm"),
            _ => "= sin cambios",
        };
    }
}

/// <summary>
/// Panel de medidas corporales del check-up: registra cambios físicos (altura, cuello, pecho, cintura, muslo y
/// pantorrilla) y los compara con la medición anterior.
/// </summary>
public sealed partial class BodyMeasurementsViewModel : ObservableObject
{
    private readonly IBodyMeasurementStore _store;
    private readonly ISessionContext _session;

    public BodyMeasurementsViewModel(IBodyMeasurementStore store, ISessionContext session)
    {
        _store = store;
        _session = session;

        // Aros sobre la figura de 170 px de ancho (la figura ocupa de x = 25 a 145).
        Metrics =
        [
            new(BodyRegion.Height, "Altura", 100, 250, 0, 0, 0),
            new(BodyRegion.Neck, "Cuello", 20, 70, 44, 68, 34),
            new(BodyRegion.Chest, "Pecho", 50, 200, 74, 43, 84),
            new(BodyRegion.Waist, "Cintura", 40, 200, 128, 50, 70),
            new(BodyRegion.Thigh, "Muslo", 20, 110, 218, 54, 36),
            new(BodyRegion.Calf, "Pantorrilla", 15, 70, 278, 59, 26),
        ];

        Refresh();
        Select(Metrics[0]);
    }

    public IReadOnlyList<BodyMetricViewModel> Metrics { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHeightSelected))]
    public partial BodyMetricViewModel? SelectedMetric { get; private set; }

    /// <summary>La altura se señala con una línea vertical y no con un aro.</summary>
    public bool IsHeightSelected => SelectedMetric?.Region == BodyRegion.Height;

    [ObservableProperty]
    public partial string? Error { get; private set; }

    [ObservableProperty]
    public partial string? SavedText { get; private set; }

    /// <summary>Resalta una zona en la figura.</summary>
    [RelayCommand]
    public void Select(BodyMetricViewModel? metric)
    {
        if (metric is null)
        {
            return;
        }

        foreach (BodyMetricViewModel m in Metrics)
        {
            m.IsSelected = m == metric;
        }

        SelectedMetric = metric;
    }

    /// <summary>Guarda las medidas escritas (las vacías se ignoran) como una sola sesión.</summary>
    [RelayCommand]
    private void Save()
    {
        Error = null;
        SavedText = null;

        if (_session.CurrentUser is not { } user)
        {
            Error = "Inicia sesión para guardar medidas.";
            return;
        }

        List<(BodyRegion, decimal)> values = [];
        foreach (BodyMetricViewModel metric in Metrics.Where(m => !string.IsNullOrWhiteSpace(m.Input)))
        {
            if (!decimal.TryParse(metric.Input.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value))
            {
                Error = $"{metric.Name}: escribe un número en centímetros.";
                Select(metric);
                return;
            }

            if (value < metric.Min || value > metric.Max)
            {
                Error = string.Create(CultureInfo.CurrentCulture, $"{metric.Name}: el valor debe estar entre {metric.Min:0} y {metric.Max:0} cm.");
                Select(metric);
                return;
            }

            values.Add((metric.Region, value));
        }

        if (values.Count == 0)
        {
            Error = "Escribe al menos una medida.";
            return;
        }

        _store.Add(user.UserName, values);
        foreach (BodyMetricViewModel metric in Metrics)
        {
            metric.Input = string.Empty;
        }

        Refresh();
        SavedText = values.Count == 1 ? "Medida guardada." : $"{values.Count} medidas guardadas.";
    }

    private void Refresh()
    {
        string user = _session.CurrentUser?.UserName ?? string.Empty;
        foreach (BodyMetricViewModel metric in Metrics)
        {
            metric.Refresh(_store.GetHistory(user, metric.Region));
        }
    }
}
