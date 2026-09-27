namespace GymPro.Core.Theming;

/// <summary>Geometry and typography: what makes Office feel dense and square, and Material airy and elevated.</summary>
public sealed record ThemeShape(
    double RadiusControl,
    double RadiusCard,
    double RadiusButton,
    bool UnderlineInputs,
    double InputBorder,
    double CardBorder,
    double ControlHeight,
    double RowHeight,
    string FontFamily,
    double FontSize,
    int Elevation);

/// <summary>
/// A theme is a palette of named colour tokens plus a shape. Hover, pressed, selection, soft status
/// backgrounds etc. are derived by <see cref="ThemeResolver"/> so a brand colour can be swapped in safely.
/// </summary>
public sealed record ThemeDefinition(
    string Id,
    string DisplayName,
    string Family,
    bool IsDark,
    IReadOnlyDictionary<string, string> Colors,
    ThemeShape Shape,
    bool AllowBrandAccent = true);

public static class ThemeCatalog
{
    public const string DefaultId = "fluent-light";

    /// <summary>Tokens every palette must define. "accent" / "accentSoft" are allowed as values where noted.</summary>
    public static readonly string[] RequiredTokens =
    [
        "Window", "Surface", "SurfaceAlt", "Border", "BorderStrong", "Text", "TextMuted",
        "Accent", "OnAccent", "Header" /* hex or "accent" */, "OnHeader",
        "Nav", "OnNav", "NavSelected" /* hex or "accentSoft" */,
        "Input", "InputBorder", "Hover", "Pressed",
        "Success", "Warning", "Danger", "Info",
        "GridHeader", "GridLine", "GridAlt", "Scroll", "ScrollHover", "Shadow",
    ];

    private static readonly ThemeShape Fluent = new(4, 8, 4, false, 1, 1, 32, 34, "Segoe UI Variable Text, Segoe UI", 13, 1);
    private static readonly ThemeShape Office = new(2, 2, 2, false, 1, 1, 28, 28, "Segoe UI", 12.5, 0);
    private static readonly ThemeShape Material = new(4, 6, 4, true, 1, 0, 36, 40, "Roboto, Segoe UI", 13.5, 2);
    private static readonly ThemeShape Dense = new(0, 0, 2, false, 1, 1, 26, 26, "Segoe UI", 12, 0);
    private static readonly ThemeShape Contrast = new(0, 0, 0, false, 2, 2, 34, 34, "Segoe UI", 14, 0);

    public static IReadOnlyList<ThemeDefinition> All { get; } =
    [
        new("fluent-light", "Modern Light", "Modern (Fluent)", false, new Dictionary<string, string>
        {
            ["Window"] = "#F3F3F3", ["Surface"] = "#FFFFFF", ["SurfaceAlt"] = "#F9F9F9", ["Border"] = "#E5E5E5", ["BorderStrong"] = "#C4C4C4",
            ["Text"] = "#1B1B1B", ["TextMuted"] = "#5C5C5C", ["Accent"] = "#0067C0", ["OnAccent"] = "#FFFFFF",
            ["Header"] = "#FFFFFF", ["OnHeader"] = "#1B1B1B", ["Nav"] = "#EDEDED", ["OnNav"] = "#1B1B1B", ["NavSelected"] = "#FFFFFF",
            ["Input"] = "#FFFFFF", ["InputBorder"] = "#8F8F8F", ["Hover"] = "#0F000000", ["Pressed"] = "#1C000000",
            ["Success"] = "#0F7B0F", ["Warning"] = "#8A5300", ["Danger"] = "#C42B1C", ["Info"] = "#0067C0",
            ["GridHeader"] = "#FAFAFA", ["GridLine"] = "#EDEDED", ["GridAlt"] = "#FBFBFB", ["Scroll"] = "#55000000", ["ScrollHover"] = "#88000000", ["Shadow"] = "#000000",
        }, Fluent),

        new("fluent-dark", "Modern Dark", "Modern (Fluent)", true, new Dictionary<string, string>
        {
            ["Window"] = "#1C1C1C", ["Surface"] = "#2B2B2B", ["SurfaceAlt"] = "#303030", ["Border"] = "#3B3B3B", ["BorderStrong"] = "#5A5A5A",
            ["Text"] = "#FFFFFF", ["TextMuted"] = "#C8C8C8", ["Accent"] = "#60CDFF", ["OnAccent"] = "#000000",
            ["Header"] = "#202020", ["OnHeader"] = "#FFFFFF", ["Nav"] = "#202020", ["OnNav"] = "#FFFFFF", ["NavSelected"] = "#2D2D2D",
            ["Input"] = "#2D2D2D", ["InputBorder"] = "#8A8A8A", ["Hover"] = "#15FFFFFF", ["Pressed"] = "#0BFFFFFF",
            ["Success"] = "#6CCB5F", ["Warning"] = "#FCE100", ["Danger"] = "#FF99A4", ["Info"] = "#60CDFF",
            ["GridHeader"] = "#2F2F2F", ["GridLine"] = "#3A3A3A", ["GridAlt"] = "#2E2E2E", ["Scroll"] = "#66FFFFFF", ["ScrollHover"] = "#99FFFFFF", ["Shadow"] = "#000000",
        }, Fluent),

        new("office-colorful", "Office Colorful", "Office", false, new Dictionary<string, string>
        {
            ["Window"] = "#F3F2F1", ["Surface"] = "#FFFFFF", ["SurfaceAlt"] = "#FAF9F8", ["Border"] = "#E1DFDD", ["BorderStrong"] = "#8A8886",
            ["Text"] = "#201F1E", ["TextMuted"] = "#605E5C", ["Accent"] = "#2B579A", ["OnAccent"] = "#FFFFFF",
            ["Header"] = "accent", ["OnHeader"] = "#FFFFFF", ["Nav"] = "#FAF9F8", ["OnNav"] = "#201F1E", ["NavSelected"] = "accentSoft",
            ["Input"] = "#FFFFFF", ["InputBorder"] = "#8A8886", ["Hover"] = "#0D000000", ["Pressed"] = "#1A000000",
            ["Success"] = "#107C10", ["Warning"] = "#8A5300", ["Danger"] = "#A4262C", ["Info"] = "#005A9E",
            ["GridHeader"] = "#F3F2F1", ["GridLine"] = "#EDEBE9", ["GridAlt"] = "#FAF9F8", ["Scroll"] = "#66000000", ["ScrollHover"] = "#99000000", ["Shadow"] = "#000000",
        }, Office),

        new("office-black", "Office Black", "Office", true, new Dictionary<string, string>
        {
            ["Window"] = "#1F1F1F", ["Surface"] = "#292929", ["SurfaceAlt"] = "#2F2F2F", ["Border"] = "#3D3D3D", ["BorderStrong"] = "#6E6E6E",
            ["Text"] = "#F3F2F1", ["TextMuted"] = "#C8C6C4", ["Accent"] = "#2F6FC2", ["OnAccent"] = "#FFFFFF",
            ["Header"] = "#0F0F0F", ["OnHeader"] = "#FFFFFF", ["Nav"] = "#262626", ["OnNav"] = "#F3F2F1", ["NavSelected"] = "#3B3B3B",
            ["Input"] = "#1F1F1F", ["InputBorder"] = "#8A8A8A", ["Hover"] = "#1AFFFFFF", ["Pressed"] = "#10FFFFFF",
            ["Success"] = "#6BC46B", ["Warning"] = "#F2C94C", ["Danger"] = "#FF9AA2", ["Info"] = "#8DBDF0",
            ["GridHeader"] = "#333333", ["GridLine"] = "#3D3D3D", ["GridAlt"] = "#2D2D2D", ["Scroll"] = "#66FFFFFF", ["ScrollHover"] = "#99FFFFFF", ["Shadow"] = "#000000",
        }, Office),

        new("material-light", "Material Light", "Material Design", false, new Dictionary<string, string>
        {
            ["Window"] = "#FAFAFA", ["Surface"] = "#FFFFFF", ["SurfaceAlt"] = "#F5F5F5", ["Border"] = "#E0E0E0", ["BorderStrong"] = "#9E9E9E",
            ["Text"] = "#212121", ["TextMuted"] = "#616161", ["Accent"] = "#3F51B5", ["OnAccent"] = "#FFFFFF",
            ["Header"] = "accent", ["OnHeader"] = "#FFFFFF", ["Nav"] = "#FFFFFF", ["OnNav"] = "#212121", ["NavSelected"] = "accentSoft",
            ["Input"] = "#F3F3F3", ["InputBorder"] = "#8C8C8C", ["Hover"] = "#0F000000", ["Pressed"] = "#1F000000",
            ["Success"] = "#1B5E20", ["Warning"] = "#A04600", ["Danger"] = "#C62828", ["Info"] = "#01579B",
            ["GridHeader"] = "#FFFFFF", ["GridLine"] = "#E0E0E0", ["GridAlt"] = "#FAFAFA", ["Scroll"] = "#55000000", ["ScrollHover"] = "#88000000", ["Shadow"] = "#000000",
        }, Material),

        new("material-dark", "Material Dark", "Material Design", true, new Dictionary<string, string>
        {
            ["Window"] = "#121212", ["Surface"] = "#1E1E1E", ["SurfaceAlt"] = "#232323", ["Border"] = "#333333", ["BorderStrong"] = "#6B6B6B",
            ["Text"] = "#EDEDED", ["TextMuted"] = "#B3B3B3", ["Accent"] = "#9FA8DA", ["OnAccent"] = "#000000",
            ["Header"] = "#272727", ["OnHeader"] = "#FFFFFF", ["Nav"] = "#1A1A1A", ["OnNav"] = "#EDEDED", ["NavSelected"] = "accentSoft",
            ["Input"] = "#2A2A2A", ["InputBorder"] = "#8A8A8A", ["Hover"] = "#14FFFFFF", ["Pressed"] = "#1FFFFFFF",
            ["Success"] = "#81C784", ["Warning"] = "#FFB74D", ["Danger"] = "#EF9A9A", ["Info"] = "#4FC3F7",
            ["GridHeader"] = "#1E1E1E", ["GridLine"] = "#333333", ["GridAlt"] = "#222222", ["Scroll"] = "#66FFFFFF", ["ScrollHover"] = "#99FFFFFF", ["Shadow"] = "#000000",
        }, Material),

        new("pro-blue", "Pro Blue", "DevExpress-style", false, new Dictionary<string, string>
        {
            ["Window"] = "#E9EEF6", ["Surface"] = "#FFFFFF", ["SurfaceAlt"] = "#F5F8FC", ["Border"] = "#CCD5E3", ["BorderStrong"] = "#8E9DB5",
            ["Text"] = "#1E2B3C", ["TextMuted"] = "#526277", ["Accent"] = "#006CBE", ["OnAccent"] = "#FFFFFF",
            ["Header"] = "#293955", ["OnHeader"] = "#FFFFFF", ["Nav"] = "#2F4260", ["OnNav"] = "#E4EAF4", ["NavSelected"] = "#46628F",
            ["Input"] = "#FFFFFF", ["InputBorder"] = "#7C8BA3", ["Hover"] = "#12003366", ["Pressed"] = "#22003366",
            ["Success"] = "#2A7A3B", ["Warning"] = "#8A5300", ["Danger"] = "#B42318", ["Info"] = "#006CBE",
            ["GridHeader"] = "#E4EAF3", ["GridLine"] = "#DCE3EE", ["GridAlt"] = "#F6F8FB", ["Scroll"] = "#66293955", ["ScrollHover"] = "#AA293955", ["Shadow"] = "#1E2B3C",
            ["NavHover"] = "#22FFFFFF", ["OnNavSelected"] = "#FFFFFF",
        }, Dense),

        new("high-contrast", "High Contrast", "Accessibility", true, new Dictionary<string, string>
        {
            ["Window"] = "#000000", ["Surface"] = "#000000", ["SurfaceAlt"] = "#000000", ["Border"] = "#FFFFFF", ["BorderStrong"] = "#FFFFFF",
            ["Text"] = "#FFFFFF", ["TextMuted"] = "#FFFFFF", ["Accent"] = "#FFFF00", ["OnAccent"] = "#000000",
            ["Header"] = "#000000", ["OnHeader"] = "#FFFFFF", ["Nav"] = "#000000", ["OnNav"] = "#FFFFFF", ["NavSelected"] = "#FFFF00",
            ["Input"] = "#000000", ["InputBorder"] = "#FFFFFF", ["Hover"] = "#40FFFF00", ["Pressed"] = "#66FFFF00",
            ["Success"] = "#3FF23F", ["Warning"] = "#FFFF00", ["Danger"] = "#FF7A7A", ["Info"] = "#1AEBFF",
            ["GridHeader"] = "#000000", ["GridLine"] = "#FFFFFF", ["GridAlt"] = "#000000", ["Scroll"] = "#FFFFFF", ["ScrollHover"] = "#FFFF00", ["Shadow"] = "#000000",
            ["OnNavSelected"] = "#000000", ["Selection"] = "#FFFF00", ["OnSelection"] = "#000000",
        }, Contrast, AllowBrandAccent: false),
    ];

    public static ThemeDefinition Find(string? id) =>
        All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? All.First(t => t.Id == DefaultId);
}
