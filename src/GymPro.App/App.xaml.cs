using System.IO;
using System.Windows;
using System.Windows.Threading;
using GymPro.App.Infrastructure;
using GymPro.App.Theming;
using GymPro.App.ViewModels;
using GymPro.App.Views;
using GymPro.Core.Configuration;
using GymPro.Data;
using GymPro.Data.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GymPro.App;

public partial class App : Application
{
    private IHost? _host;

    public static IServiceProvider Services => ((App)Current)._host!.Services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        try
        {
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
            builder.Logging.ClearProviders(); // no console in a WinExe; don't echo SQL anywhere by default
            builder.Logging.AddDebug();
            builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);

            var dbOptions = builder.Configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
            builder.Services.Configure<BrandingOptions>(builder.Configuration.GetSection(BrandingOptions.SectionName));
            builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection(SecurityOptions.SectionName));
            builder.Services.AddSingleton(Options.Create(dbOptions));
            builder.Services.AddGymData(dbOptions);
            builder.Services.AddSingleton<Branding>();
            builder.Services.AddSingleton<IImageProcessor, WpfImageProcessor>();
            builder.Services.AddTransient<MainViewModel>();
            builder.Services.AddTransient<CheckInViewModel>();
            builder.Services.AddTransient<MembersViewModel>();
            builder.Services.AddTransient<PlansViewModel>();
            builder.Services.AddTransient<UsersViewModel>();
            builder.Services.AddTransient<AuditViewModel>();
            builder.Services.AddTransient<ImportViewModel>();
            _host = builder.Build();

            await DbInitializer.InitializeAsync(Services.GetRequiredService<IDbContextFactory<GymDbContext>>(), dbOptions);

            // Theme before any window: last theme used on this PC, else the gym's default.
            var brandingOptions = Services.GetRequiredService<IOptions<BrandingOptions>>().Value;
            var local = LocalUiSettings.Load();
            ThemeManager.Instance.BrandAccent = brandingOptions.AccentColor;
            ThemeManager.Instance.Apply(local.LastTheme ?? brandingOptions.DefaultTheme, local.LastUseBrandAccent);

            // Members without a photo show the gym's logo (or the GymPro logo).
            Resources["Image.MemberPlaceholder"] = Services.GetRequiredService<Branding>().PlaceholderLogo;

            await ShowLoginAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"GymPro could not start:\n\n{ex.Message}\n\nCheck appsettings.json (Database:Path) and folder permissions.",
                "GymPro", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    /// <summary>Login -> (forced password change) -> main window. Called again on logout.</summary>
    public async Task ShowLoginAsync()
    {
        var auth = Services.GetRequiredService<AuthService>();
        var login = new LoginWindow(auth, Services.GetRequiredService<Branding>(), firstRun: !await auth.HasAnyUserAsync());
        if (login.ShowDialog() != true || login.SignedInUser is null)
        {
            Shutdown();
            return;
        }

        if (login.SignedInUser.MustChangePassword)
        {
            var change = new ChangePasswordWindow(auth, mustChange: true);
            if (change.ShowDialog() != true)
            {
                await auth.LogoutAsync();
                Shutdown();
                return;
            }
        }

        // Each staff member's own theme follows them to any PC.
        var brandingDefault = Services.GetRequiredService<IOptions<BrandingOptions>>().Value.DefaultTheme;
        ThemeManager.Instance.Apply(login.SignedInUser.ThemeId ?? brandingDefault, login.SignedInUser.UseBrandAccent);

        var main = new MainWindow(Services.GetRequiredService<MainViewModel>());
        MainWindow = main;
        main.Show();
        StartThumbnailBackfill();
    }

    /// <summary>
    /// Imported or older photos have no thumbnail yet; create them quietly in the background so the member
    /// grid is fast. Resumable: if the app closes midway it continues next time.
    /// </summary>
    private static void StartThumbnailBackfill()
    {
        var photos = Services.GetRequiredService<PhotoMaintenanceService>();
        _ = Task.Run(async () =>
        {
            try
            {
                var created = await photos.BackfillThumbnailsAsync();
                if (created > 0)
                {
                    Current.Dispatcher.Invoke(() => Toast.Show($"Prepared {created:N0} photo thumbnails."));
                }
            }
            catch (Exception ex)
            {
                LogCrash(ex); // background nicety: log, never interrupt the user
            }
        });
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash(e.Exception);
        MessageBox.Show("Unexpected error: " + e.Exception.Message, "GymPro", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void LogCrash(Exception ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GymPro", "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, $"error-{DateTime.Now:yyyyMMdd}.log"), $"[{DateTime.Now:O}] {ex}\n\n");
        }
        catch (IOException)
        {
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            try
            {
                // Off the UI thread so the dispatcher (already shutting down) can't deadlock the audit write.
                var auth = Services.GetRequiredService<AuthService>();
                Task.Run(() => auth.LogoutAsync()).Wait(TimeSpan.FromSeconds(3));
            }
            catch (Exception ex)
            {
                LogCrash(ex);
            }

            _host.Dispose();
        }

        base.OnExit(e);
    }
}
