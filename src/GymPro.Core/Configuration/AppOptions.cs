namespace GymPro.Core.Configuration;

/// <summary>
/// <c>appsettings.json</c> -> <c>"Branding"</c>: everything the end user sees as the product's identity.
/// Written by Brand Studio, the branding tool or <c>tools/publish.ps1 -Brand</c>. Property names match
/// <see cref="Branding.BrandProfile"/>, so a brand.json can be copied into this section as-is.
/// Older files that only have AppTitle/LogoPath/LogoBase64/AccentColor/DefaultTheme load unchanged.
/// </summary>
public sealed class BrandingOptions
{
    public const string SectionName = "Branding";

    /// <summary>Legacy name of <see cref="ProductName"/>; still read (and written) for older app versions.</summary>
    public string? AppTitle { get; set; }

    public string? ProductName { get; set; }
    public string? ShortName { get; set; }
    public string? CompanyName { get; set; }
    public string? Copyright { get; set; }
    public string? Description { get; set; }
    public string? WebsiteUrl { get; set; }
    public string? SupportEmail { get; set; }
    public string? SupportPhone { get; set; }
    public string? LoginWelcome { get; set; }
    public bool ShowPoweredBy { get; set; } = true;

    /// <summary>Logo file path, absolute or relative to the app folder. Ignored when <see cref="LogoBase64"/> is set.</summary>
    public string? LogoPath { get; set; }

    /// <summary>Embedded logo (PNG/JPEG bytes as Base64). Takes precedence over <see cref="LogoPath"/>.</summary>
    public string? LogoBase64 { get; set; }

    /// <summary>Window icon (.ico), absolute or relative to the app folder.</summary>
    public string? IconPath { get; set; }

    /// <summary>Accent colour, e.g. <c>#1E88E5</c>.</summary>
    public string AccentColor { get; set; } = Branding.BrandDefaults.AccentColor;

    /// <summary>Theme used on the login screen and for users who haven't picked one (see ThemeCatalog ids).</summary>
    public string DefaultTheme { get; set; } = Theming.ThemeCatalog.DefaultId;

    /// <summary>Themes offered in the picker; empty = all.</summary>
    public string[] AllowedThemes { get; set; } = [];

    /// <summary>Folder name under %ProgramData% and %LOCALAPPDATA%. The default keeps existing installs where they are.</summary>
    public string? DataFolderName { get; set; }

    /// <summary>Not used at runtime (the exe is already named); kept so brand.json round-trips through this section.</summary>
    public string? ExeName { get; set; }

    public string EffectiveProductName => FirstNonBlank(ProductName, AppTitle) ?? Branding.BrandDefaults.ProductName;

    /// <summary>Short name for captions; falls back to the product name.</summary>
    public string EffectiveShortName => FirstNonBlank(ShortName, ProductName, AppTitle) ?? Branding.BrandDefaults.ShortName;

    public string EffectiveDataFolderName => FirstNonBlank(DataFolderName) ?? Branding.BrandDefaults.DataFolderName;

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}

/// <summary><c>appsettings.json</c> → <c>"Database"</c>.</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>The placeholder in <see cref="Path"/> replaced with the brand's data folder name.</summary>
    public const string DataFolderToken = "{DataFolder}";

    /// <summary>
    /// SQLite file path. <c>{DataFolder}</c> is replaced with Branding:DataFolderName, environment variables are expanded,
    /// and relative paths are relative to the app folder.
    /// </summary>
    public string Path { get; set; } = @"%ProgramData%\{DataFolder}\gym.db";

    /// <summary>Path with the data folder filled in (environment variables are expanded later by the data layer).</summary>
    public string PathFor(string dataFolderName) => Path.Replace(DataFolderToken, dataFolderName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Seconds a writer waits for another user's write lock before failing.</summary>
    public int BusyTimeoutSeconds { get; set; } = 10;

    /// <summary><c>WAL</c> (default, one PC) or <c>DELETE</c> when the file sits on a network share, where WAL is unsafe.</summary>
    public string JournalMode { get; set; } = "WAL";
}

/// <summary><c>appsettings.json</c> → <c>"Security"</c>.</summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    public int MinPasswordLength { get; set; } = 8;
    public int MaxFailedLogins { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
}
