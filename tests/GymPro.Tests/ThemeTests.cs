using GymPro.Core.Theming;

namespace GymPro.Tests;

public class ThemeTests
{
    // null = theme's own accent; the rest are brand colours a gym might pick in the branding tool,
    // including awkward ones (pale yellow, near-black, neon green).
    public static TheoryData<string, string?> ThemesAndBrands()
    {
        var data = new TheoryData<string, string?>();
        foreach (var theme in ThemeCatalog.All)
        {
            foreach (var brand in new[] { null, "#1E88E5", "#FFEB3B", "#212121", "#39FF14", "#E53935" })
            {
                data.Add(theme.Id, brand);
            }
        }

        return data;
    }

    [Fact]
    public void Every_theme_defines_every_required_token_with_valid_colours()
    {
        Assert.Equal(ThemeCatalog.All.Count, ThemeCatalog.All.Select(t => t.Id).Distinct().Count());
        foreach (var theme in ThemeCatalog.All)
        {
            foreach (var token in ThemeCatalog.RequiredTokens)
            {
                Assert.True(theme.Colors.ContainsKey(token), $"{theme.Id} is missing {token}");
            }

            var resolved = ThemeResolver.Resolve(theme); // throws on malformed hex
            Assert.NotEmpty(resolved);
        }
    }

    [Theory]
    [MemberData(nameof(ThemesAndBrands))]
    public void Text_meets_WCAG_AA_in_every_theme_and_brand(string themeId, string? brand)
    {
        var t = ThemeResolver.Resolve(ThemeCatalog.Find(themeId), brand);
        var surface = t["Surface"];

        AssertContrast(t["Text"], surface, 4.5, "body text on surface");
        AssertContrast(t["Text"], t["Window"], 4.5, "body text on window");
        AssertContrast(t["TextMuted"], surface, 4.5, "muted text on surface");
        AssertContrast(t["OnAccent"], t["Accent"], 4.5, "primary button label");
        AssertContrast(t["OnHeader"], t["Header"], 4.5, "header text");
        AssertContrast(t["OnNav"], t["Nav"], 4.5, "navigation text");
        AssertContrast(t["OnNavSelected"], ColorMath.Over(t["NavSelected"], t["Nav"]), 4.5, "selected navigation text");
        AssertContrast(t["OnSelection"], ColorMath.Over(t["Selection"], surface), 4.5, "selected grid row text");

        foreach (var status in new[] { "Success", "Warning", "Danger", "Info" })
        {
            AssertContrast(t[status], ColorMath.Over(t[status + "Soft"], surface), 4.5, $"{status} badge text");
        }
    }

    [Theory]
    [MemberData(nameof(ThemesAndBrands))]
    public void Focus_rings_and_indicators_meet_non_text_contrast(string themeId, string? brand)
    {
        var t = ThemeResolver.Resolve(ThemeCatalog.Find(themeId), brand);
        AssertContrast(t["InputFocus"], t["Surface"], 3.0, "focus ring");
        AssertContrast(t["NavIndicator"], ColorMath.Over(t["NavSelected"], t["Nav"]), 3.0, "nav indicator");
        AssertContrast(t["InputBorder"], t["Input"], 3.0, "input border (WCAG 1.4.11)");
    }

    [Fact]
    public void Brand_colour_replaces_accent_but_high_contrast_ignores_it()
    {
        var office = ThemeResolver.Resolve(ThemeCatalog.Find("office-colorful"), "#E53935");
        Assert.Equal("#E53935", office["Accent"].ToString());
        Assert.Equal("#E53935", office["Header"].ToString()); // Office paints the header with the accent

        var hc = ThemeResolver.Resolve(ThemeCatalog.Find("high-contrast"), "#E53935");
        Assert.Equal("#FFFF00", hc["Accent"].ToString());
    }

    [Fact]
    public void Unknown_theme_id_falls_back_to_default() =>
        Assert.Equal(ThemeCatalog.DefaultId, ThemeCatalog.Find("does-not-exist").Id);

    [Fact]
    public void Contrast_math_matches_known_WCAG_values()
    {
        Assert.Equal(21.0, ColorMath.Contrast(ColorMath.Black, ColorMath.White), 1);
        Assert.Equal(4.54, ColorMath.Contrast(Rgba.Parse("#767676"), ColorMath.White), 2);
    }

    private static void AssertContrast(Rgba fg, Rgba bg, double min, string what)
    {
        var ratio = ColorMath.Contrast(fg, bg);
        Assert.True(ratio >= min, $"{what}: {fg} on {bg} = {ratio:0.00}:1 (needs {min}:1)");
    }
}
