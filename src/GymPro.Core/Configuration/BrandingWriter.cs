using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GymPro.Core.Configuration;

/// <summary>
/// Builds the <c>"Branding"</c> section and merges it into an existing appsettings.json
/// without touching the other sections (Database, Security, ...).
/// </summary>
public static partial class BrandingWriter
{
    public const int MaxTitleLength = 60;

    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // Arabic titles stay readable in the file
    };

    [GeneratedRegex("^#([0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")]
    private static partial Regex HexColor();

    public static IReadOnlyList<string> Validate(string? title, string? accentColor)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(title))
        {
            errors.Add("Title is required.");
        }
        else if (title.Trim().Length > MaxTitleLength)
        {
            errors.Add($"Title must be at most {MaxTitleLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(accentColor) || !HexColor().IsMatch(accentColor.Trim()))
        {
            errors.Add("Accent colour must look like #1E88E5.");
        }

        return errors;
    }

    /// <summary>Exactly one of <paramref name="logoPath"/> / <paramref name="logoBase64"/> should be set (or neither).</summary>
    public static JsonObject BuildSection(string title, string accentColor, string? logoPath, string? logoBase64, string? defaultTheme = null) => new()
    {
        [nameof(BrandingOptions.AppTitle)] = title.Trim(),
        [nameof(BrandingOptions.LogoPath)] = logoPath,
        [nameof(BrandingOptions.LogoBase64)] = logoBase64,
        [nameof(BrandingOptions.AccentColor)] = accentColor.Trim().ToUpperInvariant(),
        [nameof(BrandingOptions.DefaultTheme)] = Theming.ThemeCatalog.Find(defaultTheme).Id, // unknown ids fall back to the default
    };

    /// <summary>
    /// The full white-label section for a brand. Image paths are what the app should read
    /// (e.g. <c>Branding/logo.png</c>, relative to the app folder). AppTitle is written too, for older app versions.
    /// </summary>
    public static JsonObject BuildSection(GymPro.Core.Branding.BrandProfile p, string? logoPath, string? logoBase64, string? iconPath) => new()
    {
        [nameof(BrandingOptions.AppTitle)] = p.ProductName.Trim(),
        [nameof(BrandingOptions.ProductName)] = p.ProductName.Trim(),
        [nameof(BrandingOptions.ShortName)] = p.ShortName.Trim(),
        [nameof(BrandingOptions.CompanyName)] = Clean(p.CompanyName),
        [nameof(BrandingOptions.Copyright)] = Clean(p.Copyright),
        [nameof(BrandingOptions.Description)] = Clean(p.Description),
        [nameof(BrandingOptions.WebsiteUrl)] = Clean(p.WebsiteUrl),
        [nameof(BrandingOptions.SupportEmail)] = Clean(p.SupportEmail),
        [nameof(BrandingOptions.SupportPhone)] = Clean(p.SupportPhone),
        [nameof(BrandingOptions.LoginWelcome)] = Clean(p.LoginWelcome),
        [nameof(BrandingOptions.ShowPoweredBy)] = p.ShowPoweredBy,
        [nameof(BrandingOptions.LogoPath)] = logoPath,
        [nameof(BrandingOptions.LogoBase64)] = logoBase64,
        [nameof(BrandingOptions.IconPath)] = iconPath,
        [nameof(BrandingOptions.AccentColor)] = p.AccentColor.Trim().ToUpperInvariant(),
        [nameof(BrandingOptions.DefaultTheme)] = Theming.ThemeCatalog.Find(p.DefaultTheme).Id,
        [nameof(BrandingOptions.AllowedThemes)] = new JsonArray([.. p.AllowedThemes.Select(t => (JsonNode)JsonValue.Create(t))]),
        [nameof(BrandingOptions.DataFolderName)] = p.DataFolderName.Trim(),
        [nameof(BrandingOptions.ExeName)] = p.ExeName.Trim(),
    };

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    public static string ToJson(JsonNode node) => node.ToJsonString(Indented);

    /// <summary>Returns the full appsettings.json text with <c>Branding</c> replaced and everything else preserved.</summary>
    public static string Merge(string? existingJson, JsonObject brandingSection)
    {
        JsonObject root;
        try
        {
            root = string.IsNullOrWhiteSpace(existingJson)
                ? []
                : JsonNode.Parse(existingJson, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
                  ?? throw new InvalidDataException("appsettings.json must contain a JSON object.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("appsettings.json is not valid JSON: " + ex.Message, ex);
        }

        root[BrandingOptions.SectionName] = brandingSection.DeepClone();
        return ToJson(root);
    }

    /// <summary>Writes the merged file, keeping the previous version as <c>appsettings.json.bak</c>.</summary>
    public static void WriteToFile(string appSettingsPath, JsonObject brandingSection)
    {
        var existing = File.Exists(appSettingsPath) ? File.ReadAllText(appSettingsPath) : null;
        var merged = Merge(existing, brandingSection);
        if (existing is not null)
        {
            File.Copy(appSettingsPath, appSettingsPath + ".bak", overwrite: true);
        }

        var tmp = appSettingsPath + ".tmp";
        File.WriteAllText(tmp, merged);
        File.Move(tmp, appSettingsPath, overwrite: true); // atomic-ish: never leaves a half-written config
    }
}
