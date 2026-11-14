using System.Windows;

using Heartbeat.Desktop.ViewModels;

namespace Heartbeat.Desktop.Views;

public partial class IndicatorInfoWindow : Window
{
    public IndicatorInfoWindow(IndicatorInfoViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        Rect workArea = SystemParameters.WorkArea;
        Width = Math.Min(Width, workArea.Width);
        Height = Math.Min(Height, workArea.Height);
        MinWidth = Math.Min(MinWidth, workArea.Width);
        MinHeight = Math.Min(MinHeight, workArea.Height);
    }

    public IndicatorInfoViewModel ViewModel => (IndicatorInfoViewModel)DataContext;
}
