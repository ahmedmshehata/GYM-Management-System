using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using GymPro.Core.Theming;

namespace GymPro.App.Theming;

/// <summary>
/// Applies a <see cref="ThemeDefinition"/> at runtime by swapping one merged ResourceDictionary of tokens.
/// Every style uses DynamicResource, so open windows restyle instantly.
/// </summary>
public sealed partial class ThemeManager
{
    private ResourceDictionary? _tokens;
    private IReadOnlyDictionary<string, Core.Theming.Rgba> _resolved = new Dictionary<string, Core.Theming.Rgba>();

    public static ThemeManager Instance { get; } = new();

    public ThemeDefinition Theme { get; private set; } = ThemeCatalog.Find(null);
    public bool UseBrandAccent { get; private set; } = true;

    /// <summary>The gym's brand colour from appsettings.json (Branding:AccentColor).</summary>
    public string? BrandAccent { get; set; }

    public event EventHandler? ThemeChanged;

    private ThemeManager()
    {
        // Every window (login, dialogs, main) gets a title bar that matches the theme.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) => ApplyTitleBar((Window)s)));
    }

    public void Apply(string? themeId, bool useBrandAccent)
    {
        Theme = ThemeCatalog.Find(themeId);
        UseBrandAccent = useBrandAccent;
        _resolved = ThemeResolver.Resolve(Theme, useBrandAccent ? BrandAccent : null);

        var dict = BuildResources(Theme, _resolved);
        var merged = Application.Current.Resources.MergedDictionaries;
        if (_tokens is not null)
        {
            merged.Remove(_tokens);
        }

        merged.Insert(0, dict);
        _tokens = dict;

        foreach (Window w in Application.Current.Windows)
        {
            ApplyTitleBar(w);
        }

        LocalUiSettings.Save(LocalUiSettings.Load() with { LastTheme = Theme.Id, LastUseBrandAccent = useBrandAccent });
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Resolved colours for previews (theme picker swatches).</summary>
    public static Brush Swatch(ThemeDefinition theme, string token, string? brand) =>
        ToBrush(ThemeResolver.Resolve(theme, brand)[token]);

    private static ResourceDictionary BuildResources(ThemeDefinition theme, IReadOnlyDictionary<string, Core.Theming.Rgba> tokens)
    {
        var d = new ResourceDictionary();
        foreach (var (name, color) in tokens)
        {
            d["Brush." + name] = ToBrush(color);
        }

        var s = theme.Shape;
        d["Radius.Control"] = new CornerRadius(s.RadiusControl);
        d["Radius.Card"] = new CornerRadius(s.RadiusCard);
        d["Radius.Button"] = new CornerRadius(s.RadiusButton);
        d["Thickness.Input"] = s.UnderlineInputs ? new Thickness(0, 0, 0, s.InputBorder) : new Thickness(s.InputBorder);
        d["Thickness.InputFocus"] = s.UnderlineInputs ? new Thickness(0, 0, 0, 2) : new Thickness(Math.Max(s.InputBorder, 1.5));
        d["Thickness.Card"] = new Thickness(s.CardBorder);
        d["Thickness.Box"] = new Thickness(Math.Max(1.5, s.InputBorder));
        d["Size.Control"] = s.ControlHeight;
        d["Size.Row"] = s.RowHeight;
        d["Font.Family"] = new FontFamily(s.FontFamily);
        d["Font.Size"] = s.FontSize;

        var shadow = ToColor(tokens["Shadow"]);
        d["Effect.Card"] = Freeze(new DropShadowEffect
        {
            Color = shadow,
            Direction = 270,
            ShadowDepth = s.Elevation,
            BlurRadius = s.Elevation * 6,
            Opacity = s.Elevation == 0 ? 0 : theme.IsDark ? 0.45 : 0.12,
        });
        d["Effect.Popup"] = Freeze(new DropShadowEffect
        {
            Color = shadow,
            Direction = 270,
            ShadowDepth = 3,
            BlurRadius = 14,
            Opacity = theme.Id == "high-contrast" ? 0 : theme.IsDark ? 0.55 : 0.18,
        });
        return d;
    }

    private static T Freeze<T>(T f)
        where T : Freezable
    {
        f.Freeze();
        return f;
    }

    private static Color ToColor(Core.Theming.Rgba c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    private static SolidColorBrush ToBrush(Core.Theming.Rgba c) => Freeze(new SolidColorBrush(ToColor(c)));

    // ------------------------------------------------------------------ Windows title bar

    private void ApplyTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || _resolved.Count == 0)
        {
            return;
        }

        var dark = Theme.IsDark ? 1 : 0;
        if (DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref dark, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkModeLegacy, ref dark, sizeof(int)); // Windows 10 before 20H1
        }

        // Windows 11: paint the caption in the header colour so the title bar and app header read as one band.
        var header = _resolved["Header"];
        var onHeader = _resolved["OnHeader"];
        var caption = header.R | (header.G << 8) | (header.B << 16);
        var text = onHeader.R | (onHeader.G << 8) | (onHeader.B << 16);
        DwmSetWindowAttribute(hwnd, DwmCaptionColor, ref caption, sizeof(int));
        DwmSetWindowAttribute(hwnd, DwmTextColor, ref text, sizeof(int));
    }

    private const int DwmUseImmersiveDarkModeLegacy = 19;
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

/// <summary>Per-PC UI state that isn't tied to a signed-in user: theme for the login screen, nav rail width.</summary>
public sealed record LocalUiSettings(
    string? LastTheme = null,
    bool LastUseBrandAccent = true,
    bool NavCollapsed = false,
    string? LastCameraId = null,
    bool MemberCards = false)
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GymPro", "ui.json");

    public static LocalUiSettings Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<LocalUiSettings>(File.ReadAllText(FilePath)) ?? new() : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public static void Save(LocalUiSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cosmetic preference only; never fail the app over it.
        }
    }
}
