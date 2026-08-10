using System.Windows;
using System.Windows.Input;
using Wukong.Application;

namespace Wukong.Desktop;

public partial class DeveloperLoginWindow : Window
{
    private readonly IDeveloperSession _session;

    public DeveloperLoginWindow(IDeveloperSession session)
    {
        _session = session;
        InitializeComponent();
        Loaded += (_, _) => PasswordInput.Focus();
    }

    private void Login_Click(object sender, RoutedEventArgs e) => TryLogin();
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void PasswordInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        e.Handled = true;
        TryLogin();
    }

    private void TryLogin()
    {
        if (_session.Authenticate(PasswordInput.Password))
        {
            DialogResult = true;
            return;
        }
        PasswordInput.Clear();
        LoginStatus.Text = "密码错误。";
        PasswordInput.Focus();
    }
}
