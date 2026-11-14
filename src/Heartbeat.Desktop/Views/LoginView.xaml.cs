using System.Windows;
using System.Windows.Controls;

using Heartbeat.Desktop.ViewModels;

namespace Heartbeat.Desktop.Views;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => UserBox.Focus();

    // PasswordBox no admite enlace de datos (la contraseña no debe vivir en una propiedad), así que se pasa al comando.
    private void SignIn_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel viewModel)
        {
            viewModel.SignInCommand.Execute(PassBox.Password);
            PassBox.Clear();
        }
    }
}
