using System.Text.Json.Nodes;
using GymPro.Core.Configuration;
using Microsoft.Extensions.Configuration;

namespace GymPro.Tests;

public class BrandingWriterTests
{
    private const string Existing = """
        {
          // comment written by hand
          "Branding": { "AppTitle": "Old", "LogoPath": "old.png" },
          "Database": { "Path": "D:\\gym.db", "BusyTimeoutSeconds": 30 },
        }
        """;

    [Fact]
    public void Merge_replaces_branding_and_keeps_other_sections()
    {
        var merged = BrandingWriter.Merge(Existing, BrandingWriter.BuildSection("نادي الأبطال", "#ff5722", "Branding/logo.png", null));
        var root = JsonNode.Parse(merged)!;

        Assert.Equal("نادي الأبطال", (string?)root["Branding"]!["AppTitle"]);
        Assert.Equal("#FF5722", (string?)root["Branding"]!["AccentColor"]);
        Assert.Equal("Branding/logo.png", (string?)root["Branding"]!["LogoPath"]);
        Assert.Equal(30, (int)root["Database"]!["BusyTimeoutSeconds"]!);
        Assert.Contains("نادي الأبطال", merged); // not \u-escaped
    }

    [Fact]
    public void Written_file_binds_back_to_BrandingOptions_and_keeps_a_backup()
    {
        var dir = Directory.CreateTempSubdirectory("gympro-branding-");
        var file = Path.Combine(dir.FullName, "appsettings.json");
        File.WriteAllText(file, Existing);
        var logo = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 1, 2, 3]);

        BrandingWriter.WriteToFile(file, BrandingWriter.BuildSection("Iron Gym", "#112233", null, logo, "material-dark"));

        var options = new ConfigurationBuilder().AddJsonFile(file).Build()
            .GetSection(BrandingOptions.SectionName).Get<BrandingOptions>()!;
        Assert.Equal("Iron Gym", options.AppTitle);
        Assert.Equal(logo, options.LogoBase64);
        Assert.Null(options.LogoPath);
        Assert.Equal("material-dark", options.DefaultTheme);
        Assert.True(File.Exists(file + ".bak"));
        dir.Delete(recursive: true);
    }

    [Fact]
    public void Unknown_default_theme_is_replaced_with_the_catalog_default() =>
        Assert.Equal(GymPro.Core.Theming.ThemeCatalog.DefaultId,
            (string?)BrandingWriter.BuildSection("A", "#000000", null, null, "no-such-theme")["DefaultTheme"]);

    [Theory]
    [InlineData("", "#123456", 1)]
    [InlineData("Gym", "blue", 1)]
    [InlineData("Gym", "#12345", 1)]
    [InlineData("Gym", "#80123456", 0)]
    public void Validates_title_and_colour(string title, string color, int errors) =>
        Assert.Equal(errors, BrandingWriter.Validate(title, color).Count);

    [Fact]
    public void Rejects_invalid_existing_json_instead_of_overwriting_it() =>
        Assert.Throws<InvalidDataException>(() => BrandingWriter.Merge("{ not json", BrandingWriter.BuildSection("A", "#000000", null, null)));
}
