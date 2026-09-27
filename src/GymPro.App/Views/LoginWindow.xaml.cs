using System.Windows;
using GymPro.App.Infrastructure;
using GymPro.Core.Entities;
using GymPro.Data.Services;

namespace GymPro.App.Views;

public partial class LoginWindow : Window
{
    private readonly AuthService _auth;
    private readonly bool _firstRun;

    public LoginWindow(AuthService auth, Branding branding, bool firstRun)
    {
        InitializeComponent();
        _auth = auth;
        _firstRun = firstRun;

        Title = branding.Title;
        TitleText.Text = branding.Title;
        LogoImage.Source = branding.Logo;
        Icon = branding.Logo;
        LogoImage.Visibility = branding.Logo is null ? Visibility.Collapsed : Visibility.Visible;

        if (firstRun)
        {
            FirstRunHint.Visibility = DisplayNamePanel.Visibility = ConfirmPanel.Visibility = Visibility.Visible;
            OkButton.Content = "Create administrator";
            SubtitleText.Text = "Welcome! Let's set up your gym.";
            UserNameBox.Text = "admin";
        }

        Loaded += (_, _) => (firstRun ? DisplayNameBox : UserNameBox).Focus();
    }

    public AppUser? SignedInUser { get; private set; }

    private async void OnOk(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        OkButton.IsEnabled = false;
        try
        {
            if (_firstRun)
            {
                if (PasswordBox.Password != ConfirmBox.Password)
                {
                    ErrorText.Text = "Passwords do not match.";
                    return;
                }

                if (string.IsNullOrWhiteSpace(DisplayNameBox.Text) || string.IsNullOrWhiteSpace(UserNameBox.Text))
                {
                    ErrorText.Text = "Full name and user name are required.";
                    return;
                }

                await _auth.CreateFirstAdminAsync(UserNameBox.Text, DisplayNameBox.Text, PasswordBox.Password);
            }

            var result = await _auth.LoginAsync(UserNameBox.Text, PasswordBox.Password);
            if (!result.Succeeded)
            {
                ErrorText.Text = result.Error;
                PasswordBox.Clear();
                PasswordBox.Focus();
                return;
            }

            SignedInUser = result.User;
            DialogResult = true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            ErrorText.Text = ex.Message;
        }
        finally
        {
            OkButton.IsEnabled = true;
        }
    }
}
