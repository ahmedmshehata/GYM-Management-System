using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymPro.App.Infrastructure;
using GymPro.App.Theming;
using GymPro.Core.Security;
using GymPro.Core.Theming;
using GymPro.Data.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GymPro.App.ViewModels;

/// <summary>A page in the navigation rail. <paramref name="Glyph"/> is a Segoe Fluent Icons code point.</summary>
public sealed record NavItem(string Glyph, string Title, string Shortcut, object Page);

/// <summary>A theme card in the appearance picker, pre-coloured with the resolved tokens.</summary>
public sealed record ThemeOption(ThemeDefinition Theme, Brush Header, Brush Nav, Brush Window, Brush Surface, Brush Accent, Brush Text)
{
    public string Id => Theme.Id;
    public string Name => Theme.DisplayName;
    public string Family => Theme.Family;
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly AuthService _auth;
    private readonly ICurrentUser _user;
    private bool _loadingTheme;

    public MainViewModel(IServiceProvider services, ICurrentUser user, Branding branding, AuthService auth)
    {
        _auth = auth;
        _user = user;
        Branding = branding;
        UserName = user.UserName ?? "";
        RoleText = user.Role?.ToString() ?? "";

        void Add(Permission p, string glyph, string title, Func<object> page)
        {
            if (Permissions.Has(user.Role, p))
            {
                Pages.Add(new NavItem(glyph, title, $"Ctrl+{Pages.Count + 1}", page()));
            }
        }

        Add(Permission.CheckIn, "", "Check-in", services.GetRequiredService<CheckInViewModel>);
        Add(Permission.ManageMembers, "", "Members", services.GetRequiredService<MembersViewModel>);
        Add(Permission.ManagePlans, "", "Plans", services.GetRequiredService<PlansViewModel>);
        Add(Permission.ManageUsers, "", "Staff users", services.GetRequiredService<UsersViewModel>);
        Add(Permission.ViewAudit, "", "Audit log", services.GetRequiredService<AuditViewModel>);
        Add(Permission.ImportLegacy, "", "Import & maintenance", services.GetRequiredService<ImportViewModel>);
        SelectedPage = Pages.FirstOrDefault();

        IsNavCollapsed = LocalUiSettings.Load().NavCollapsed;
        UseBrandAccent = ThemeManager.Instance.UseBrandAccent;
        RebuildThemeOptions();
    }

    public Branding Branding { get; }
    public string UserName { get; }
    public string RoleText { get; }
    public ObservableCollection<NavItem> Pages { get; } = [];
    public ObservableCollection<ThemeOption> Themes { get; } = [];

    [ObservableProperty]
    public partial NavItem? SelectedPage { get; set; }

    [ObservableProperty]
    public partial bool IsNavCollapsed { get; set; }

    [ObservableProperty]
    public partial ThemeOption? SelectedTheme { get; set; }

    [ObservableProperty]
    public partial bool UseBrandAccent { get; set; }

    public bool BrandAccentAllowed => SelectedTheme?.Theme.AllowBrandAccent ?? true;

    partial void OnIsNavCollapsedChanged(bool value) =>
        LocalUiSettings.Save(LocalUiSettings.Load() with { NavCollapsed = value });

    [RelayCommand]
    private void ToggleNav() => IsNavCollapsed = !IsNavCollapsed;

    /// <summary>Ctrl+1..9 jumps to the n-th page the user can see.</summary>
    [RelayCommand]
    private void Navigate(string? index)
    {
        if (int.TryParse(index, out var i) && i >= 1 && i <= Pages.Count)
        {
            SelectedPage = Pages[i - 1];
        }
    }

    partial void OnSelectedThemeChanged(ThemeOption? value)
    {
        OnPropertyChanged(nameof(BrandAccentAllowed));
        if (value is not null && !_loadingTheme)
        {
            _ = ApplyThemeAsync();
        }
    }

    partial void OnUseBrandAccentChanged(bool value)
    {
        if (_loadingTheme)
        {
            return;
        }

        RebuildThemeOptions(); // swatches preview the brand colour too
        _ = ApplyThemeAsync();
    }

    private async Task ApplyThemeAsync()
    {
        if (SelectedTheme is not { } t)
        {
            return;
        }

        ThemeManager.Instance.Apply(t.Id, UseBrandAccent);
        try
        {
            await _auth.SavePreferencesAsync(t.Id, UseBrandAccent);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            Toast.Show("Theme applied, but it couldn't be saved to your profile.", Core.Rules.StatusTone.Warning);
        }
    }

    private void RebuildThemeOptions()
    {
        _loadingTheme = true;
        var brand = UseBrandAccent ? ThemeManager.Instance.BrandAccent : null;
        Themes.Clear();
        foreach (var t in ThemeCatalog.All)
        {
            Themes.Add(new ThemeOption(t,
                ThemeManager.Swatch(t, "Header", brand), ThemeManager.Swatch(t, "Nav", brand), ThemeManager.Swatch(t, "Window", brand),
                ThemeManager.Swatch(t, "Surface", brand), ThemeManager.Swatch(t, "Accent", brand), ThemeManager.Swatch(t, "Text", brand)));
        }

        SelectedTheme = Themes.FirstOrDefault(o => o.Id == ThemeManager.Instance.Theme.Id);
        _loadingTheme = false;
    }

    public Task SignOutAsync() => _auth.LogoutAsync();

    public bool IsSignedIn => _user.IsAuthenticated;
}
