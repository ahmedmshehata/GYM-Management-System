namespace GymPro.Core.Configuration;

/// <summary><c>appsettings.json</c> → <c>"Branding"</c>. Written by the GymPro.Branding tool.</summary>
public sealed class BrandingOptions
{
    public const string SectionName = "Branding";

    public string AppTitle { get; set; } = "GymPro";

    /// <summary>Logo file path, absolute or relative to the app folder. Ignored when <see cref="LogoBase64"/> is set.</summary>
    public string? LogoPath { get; set; }

    /// <summary>Embedded logo (PNG/JPEG bytes as Base64). Takes precedence over <see cref="LogoPath"/>.</summary>
    public string? LogoBase64 { get; set; }

    /// <summary>Accent colour, e.g. <c>#1E88E5</c>.</summary>
    public string AccentColor { get; set; } = "#1E88E5";

    /// <summary>Theme used on the login screen and for users who haven't picked one (see ThemeCatalog ids).</summary>
    public string DefaultTheme { get; set; } = Theming.ThemeCatalog.DefaultId;
}

/// <summary><c>appsettings.json</c> → <c>"Database"</c>.</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>SQLite file path. Environment variables are expanded; relative paths are relative to the app folder.</summary>
    public string Path { get; set; } = @"%ProgramData%\GymPro\gym.db";

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
