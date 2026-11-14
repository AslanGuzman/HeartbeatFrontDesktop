using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Heartbeat.Desktop.ViewModels;

namespace Heartbeat.Desktop.Views;

public partial class BodyPanelView : UserControl
{
    public BodyPanelView()
    {
        InitializeComponent();
    }

    // Al enfocar el campo de una zona se resalta en la figura.
    private void OnFieldFocused(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is BodyMeasurementsViewModel body && (e.OriginalSource as FrameworkElement)?.DataContext is BodyMetricViewModel metric)
        {
            body.Select(metric);
        }
    }
}
