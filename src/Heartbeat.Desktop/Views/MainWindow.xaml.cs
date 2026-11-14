using System.Windows;

using Heartbeat.Desktop.ViewModels;

namespace Heartbeat.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // En pantallas pequeñas (por ejemplo 1366x768) el tamaño por defecto no cabe: se ajusta al área de trabajo
        // para que la barra de estado nunca quede fuera de la pantalla.
        Rect workArea = SystemParameters.WorkArea;
        Width = Math.Min(Width, workArea.Width);
        Height = Math.Min(Height, workArea.Height);
        MinWidth = Math.Min(MinWidth, workArea.Width);
        MinHeight = Math.Min(MinHeight, workArea.Height);
    }
}
