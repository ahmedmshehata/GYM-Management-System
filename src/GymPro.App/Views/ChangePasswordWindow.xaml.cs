using System.Windows;
using GymPro.Data.Services;

namespace GymPro.App.Views;

public partial class ChangePasswordWindow : Window
{
    private readonly AuthService _auth;

    public ChangePasswordWindow(AuthService auth, bool mustChange)
    {
        InitializeComponent();
        _auth = auth;
        Hint.Visibility = mustChange ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => CurrentBox.Focus();
    }

    private async void OnOk(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        if (NewBox.Password != ConfirmBox.Password)
        {
            ErrorText.Text = "New passwords do not match.";
            return;
        }

        try
        {
            await _auth.ChangePasswordAsync(CurrentBox.Password, NewBox.Password);
            DialogResult = true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            ErrorText.Text = ex.Message;
        }
    }
}
