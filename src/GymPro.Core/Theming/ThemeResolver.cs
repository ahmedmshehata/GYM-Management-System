namespace GymPro.Core.Theming;

/// <summary>
/// Turns a <see cref="ThemeDefinition"/> (+ optional brand colour) into the full token set the UI binds to.
/// All derived colours are computed here so every theme stays readable even with an arbitrary brand colour.
/// </summary>
public static class ThemeResolver
{
    public static IReadOnlyDictionary<string, Rgba> Resolve(ThemeDefinition theme, string? brandAccent = null)
    {
        var c = theme.Colors;
        Rgba Get(string key) => Rgba.Parse(c[key]);
        Rgba? Opt(string key) => c.TryGetValue(key, out var v) ? Rgba.Parse(v) : null;

        var surface = Get("Surface");
        var useBrand = theme.AllowBrandAccent && Rgba.TryParse(brandAccent, out _);
        var accent = useBrand ? Rgba.Parse(brandAccent!) with { A = 255 } : Get("Accent");
        var onAccent = useBrand ? ColorMath.BestTextOn(accent) : Get("OnAccent");

        // A pale brand colour (e.g. yellow) can fill a button, but focus rings and nav indicators
        // must still be visible against the surface (WCAG 1.4.11 non-text contrast >= 3:1).
        var accentStrong = ColorMath.EnsureContrast(accent, surface, 3.0);

        var t = new Dictionary<string, Rgba>
        {
            ["Window"] = Get("Window"),
            ["Surface"] = surface,
            ["SurfaceAlt"] = Get("SurfaceAlt"),
            ["Border"] = Get("Border"),
            ["BorderStrong"] = Get("BorderStrong"),
            ["Text"] = Get("Text"),
            ["TextMuted"] = Get("TextMuted"),
            ["TextDisabled"] = ColorMath.WithAlpha(Get("Text"), 0x70),
            ["Accent"] = accent,
            ["AccentStrong"] = accentStrong,
            ["OnAccent"] = onAccent,
            ["AccentHover"] = theme.IsDark ? ColorMath.Mix(accent, ColorMath.White, 0.12) : ColorMath.Mix(accent, ColorMath.Black, 0.10),
            ["AccentPressed"] = theme.IsDark ? ColorMath.Mix(accent, ColorMath.White, 0.24) : ColorMath.Mix(accent, ColorMath.Black, 0.20),
            ["AccentSoft"] = ColorMath.WithAlpha(accentStrong, theme.IsDark ? (byte)0x40 : (byte)0x22),
            ["Input"] = Get("Input"),
            ["InputBorder"] = Get("InputBorder"),
            ["InputFocus"] = accentStrong,
            ["Hover"] = Get("Hover"),
            ["Pressed"] = Get("Pressed"),
            ["GridHeader"] = Get("GridHeader"),
            ["GridLine"] = Get("GridLine"),
            ["GridAlt"] = Get("GridAlt"),
            ["Scroll"] = Get("Scroll"),
            ["ScrollHover"] = Get("ScrollHover"),
            ["Shadow"] = Get("Shadow"),
        };

        t["Selection"] = Opt("Selection") ?? t["AccentSoft"];
        t["OnSelection"] = Opt("OnSelection") ?? t["Text"];

        var headerIsAccent = c["Header"] == "accent";
        t["Header"] = headerIsAccent ? accent : Get("Header");
        t["OnHeader"] = headerIsAccent && useBrand ? onAccent : Get("OnHeader");
        t["HeaderHover"] = ColorMath.WithAlpha(t["OnHeader"], 0x24);
        t["HeaderMuted"] = ColorMath.WithAlpha(t["OnHeader"], 0xCC);

        t["Nav"] = Get("Nav");
        t["OnNav"] = Get("OnNav");
        t["NavHover"] = Opt("NavHover") ?? t["Hover"];
        t["NavSelected"] = c["NavSelected"] == "accentSoft"
            ? ColorMath.Over(ColorMath.WithAlpha(accentStrong, theme.IsDark ? (byte)0x40 : (byte)0x1E), t["Nav"])
            : Get("NavSelected");
        t["OnNavSelected"] = Opt("OnNavSelected") ?? t["OnNav"];
        t["NavIndicator"] = ColorMath.EnsureContrast(accent, t["NavSelected"], 3.0);

        foreach (var status in new[] { "Success", "Warning", "Danger", "Info" })
        {
            var color = Get(status);
            t[status] = color;
            t[status + "Soft"] = ColorMath.WithAlpha(color, theme.IsDark ? (byte)0x30 : (byte)0x1C);
        }

        return t;
    }
}
