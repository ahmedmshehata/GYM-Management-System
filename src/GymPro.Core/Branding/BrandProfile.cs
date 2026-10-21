using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GymPro.Core.Theming;

namespace GymPro.Core.Branding;

/// <summary>Values used when no brand is configured: the plain GymPro product.</summary>
public static class BrandDefaults
{
    public const string ProductName = "GymPro";
    public const string ShortName = "GymPro";
    public const string DataFolderName = "GymPro";
    public const string ExeName = "GymPro";
    public const string AccentColor = "#1E88E5";

    /// <summary>The original product, credited in About → Licences whatever the brand.</summary>
    public const string UpstreamProduct = "GymPro";
    public const string UpstreamCopyright = "Copyright (c) 2026 Ahmed Shehata";
    public const string UpstreamUrl = "https://github.com/ahmedmshehata/GYM-Management-System";
}

/// <summary>
/// A reseller's brand: everything an end user sees instead of "GymPro".
/// Stored as <c>brand.json</c> in a brand pack. Property names deliberately match <see cref="Configuration.BrandingOptions"/>,
/// so the same JSON can be dropped into the <c>"Branding"</c> section of appsettings.json.
/// </summary>
public sealed record BrandProfile
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string ProductName { get; init; } = BrandDefaults.ProductName;
    public string ShortName { get; init; } = BrandDefaults.ShortName;
    public string? CompanyName { get; init; }
    public string? Copyright { get; init; }
    public string? Description { get; init; }
    public string? WebsiteUrl { get; init; }
    public string? SupportEmail { get; init; }
    public string? SupportPhone { get; init; }
    public string? LoginWelcome { get; init; }
    public bool ShowPoweredBy { get; init; } = true;

    /// <summary>Logo file, relative to the brand pack (normally <c>logo.png</c>).</summary>
    public string? LogoPath { get; init; }

    /// <summary>Windows icon, relative to the brand pack (normally <c>app.ico</c>).</summary>
    public string? IconPath { get; init; }

    public string AccentColor { get; init; } = BrandDefaults.AccentColor;
    public string DefaultTheme { get; init; } = ThemeCatalog.DefaultId;

    /// <summary>Themes offered in the picker; empty = all.</summary>
    public IReadOnlyList<string> AllowedThemes { get; init; } = [];

    /// <summary>Folder under %ProgramData% (database) and %LOCALAPPDATA% (logs, UI settings).</summary>
    public string DataFolderName { get; init; } = BrandDefaults.DataFolderName;

    /// <summary>Executable name without <c>.exe</c>; used by tools/publish.ps1 -Brand.</summary>
    public string ExeName { get; init; } = BrandDefaults.ExeName;
}

public static partial class BrandProfileValidator
{
    public const int MaxProductName = 60;
    public const int MaxShortName = 24;
    public const int MaxLoginWelcome = 80;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    [GeneratedRegex("^#([0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")]
    private static partial Regex HexColor();

    // Letters/digits first, then letters, digits, space, dot, underscore, hyphen; no trailing dot or space.
    [GeneratedRegex(@"^[\p{L}\p{N}](?:[\p{L}\p{N} ._-]{0,38}[\p{L}\p{N}_-])?$")]
    private static partial Regex SafeFolder();

    [GeneratedRegex(@"^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,38}[A-Za-z0-9_-])?$")]
    private static partial Regex SafeExe();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex Email();

    /// <summary>Field name → message, in display order. Empty = valid.</summary>
    public static IReadOnlyList<(string Field, string Message)> Validate(BrandProfile p)
    {
        var e = new List<(string, string)>();

        Required(nameof(p.ProductName), p.ProductName, MaxProductName, "Product name");
        Required(nameof(p.ShortName), p.ShortName, MaxShortName, "Short name");

        if (p.LoginWelcome is { Length: > MaxLoginWelcome })
        {
            e.Add((nameof(p.LoginWelcome), $"Login welcome must be at most {MaxLoginWelcome} characters."));
        }

        if (!string.IsNullOrWhiteSpace(p.WebsiteUrl) &&
            !(Uri.TryCreate(p.WebsiteUrl.Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)))
        {
            e.Add((nameof(p.WebsiteUrl), "Website must be a full http(s):// address."));
        }

        if (!string.IsNullOrWhiteSpace(p.SupportEmail) && !Email().IsMatch(p.SupportEmail.Trim()))
        {
            e.Add((nameof(p.SupportEmail), "Support email doesn't look like an email address."));
        }

        if (string.IsNullOrWhiteSpace(p.AccentColor) || !HexColor().IsMatch(p.AccentColor.Trim()))
        {
            e.Add((nameof(p.AccentColor), "Accent colour must look like #1E88E5."));
        }

        var known = ThemeCatalog.All.Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!known.Contains(p.DefaultTheme))
        {
            e.Add((nameof(p.DefaultTheme), $"Unknown theme '{p.DefaultTheme}'."));
        }

        var unknown = p.AllowedThemes.Where(t => !known.Contains(t)).ToList();
        if (unknown.Count > 0)
        {
            e.Add((nameof(p.AllowedThemes), $"Unknown theme(s): {string.Join(", ", unknown)}."));
        }
        else if (p.AllowedThemes.Count > 0 && !p.AllowedThemes.Contains(p.DefaultTheme, StringComparer.OrdinalIgnoreCase))
        {
            e.Add((nameof(p.DefaultTheme), "The default theme must be one of the allowed themes."));
        }

        if (!IsSafeName(p.DataFolderName, SafeFolder()))
        {
            e.Add((nameof(p.DataFolderName), "Data folder must be a plain folder name (letters, digits, space . _ -)."));
        }

        if (!IsSafeName(p.ExeName, SafeExe()))
        {
            e.Add((nameof(p.ExeName), "Exe name may only use letters, digits, . _ - (no spaces) and must not end in .exe."));
        }
        else if (p.ExeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            e.Add((nameof(p.ExeName), "Leave out the .exe extension."));
        }

        if (p.SchemaVersion > BrandProfile.CurrentSchemaVersion)
        {
            e.Add((nameof(p.SchemaVersion), $"This brand was made by a newer version (schema {p.SchemaVersion}). Update the app."));
        }

        return e;

        void Required(string field, string? value, int max, string label)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                e.Add((field, $"{label} is required."));
            }
            else if (value.Trim().Length > max)
            {
                e.Add((field, $"{label} must be at most {max} characters."));
            }
        }
    }

    private static bool IsSafeName(string? name, Regex pattern) =>
        !string.IsNullOrWhiteSpace(name) && pattern.IsMatch(name) && !ReservedNames.Contains(Path.GetFileNameWithoutExtension(name));
}

public static class BrandJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // Arabic names stay readable
    };

    public static string Serialize(BrandProfile profile) => JsonSerializer.Serialize(profile, Options);

    public static BrandProfile Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<BrandProfile>(json, Options) ?? throw new InvalidDataException("brand.json is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("brand.json is not valid: " + ex.Message, ex);
        }
    }
}
