using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using GymPro.App.Infrastructure;
using GymPro.App.ViewModels;
using GymPro.Core.Rules;
using GymPro.Data.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GymPro.App.Views;

public sealed record ToastMessage(string Message, StatusTone Tone)
{
    public string Glyph => Tone switch
    {
        StatusTone.Success => "",
        StatusTone.Warning => "",
        StatusTone.Danger => "",
        _ => "",
    };
}

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly ObservableCollection<ToastMessage> _toasts = [];
    private bool _signingOut;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        ToastHost.ItemsSource = _toasts;
        Toast.Shown += OnToast;
        Closed += (_, _) =>
        {
            Toast.Shown -= OnToast;
            if (!_signingOut)
            {
                Application.Current.Shutdown();
            }
        };
    }

    private void OnToast(string message, StatusTone tone)
    {
        var toast = new ToastMessage(message, tone);
        _toasts.Add(toast);
        while (_toasts.Count > 3)
        {
            _toasts.RemoveAt(0);
        }

        // Success fades quickly; warnings/errors stay a little longer so they're not missed.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(tone == StatusTone.Success ? 4 : 7) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _toasts.Remove(toast);
        };
        timer.Start();
    }

    private void OnToggleThemes(object sender, RoutedEventArgs e) => ThemePopup.IsOpen = !ThemePopup.IsOpen;

    private void OnChangePassword(object sender, RoutedEventArgs e)
    {
        var ok = new ChangePasswordWindow(App.Services.GetRequiredService<AuthService>(), mustChange: false) { Owner = this }.ShowDialog();
        if (ok == true)
        {
            Toast.Show("Password changed.");
        }
    }

    private async void OnSignOut(object sender, RoutedEventArgs e)
    {
        _signingOut = true;
        await _vm.SignOutAsync();
        Close();
        await ((App)Application.Current).ShowLoginAsync();
    }
}
