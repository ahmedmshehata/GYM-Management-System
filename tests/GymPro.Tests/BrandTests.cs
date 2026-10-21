using System.Text.Json.Nodes;
using GymPro.Core.Branding;
using GymPro.Core.Configuration;
using GymPro.Core.Theming;
using Microsoft.Extensions.Configuration;

namespace GymPro.Tests;

public class BrandProfileValidationTests
{
    private static BrandProfile Valid() => new()
    {
        ProductName = "Iron Club Manager",
        ShortName = "Iron Club",
        WebsiteUrl = "https://ironclub.example",
        SupportEmail = "help@ironclub.example",
        AccentColor = "#E53935",
        DefaultTheme = "material-dark",
        AllowedThemes = ["material-light", "material-dark"],
        DataFolderName = "Iron Club",
        ExeName = "IronClub",
    };

    private static IReadOnlyList<string> Fields(BrandProfile p) => [.. BrandProfileValidator.Validate(p).Select(e => e.Field)];

    [Fact]
    public void A_complete_brand_is_valid() => Assert.Empty(BrandProfileValidator.Validate(Valid()));

    [Fact]
    public void The_default_profile_is_valid() => Assert.Empty(BrandProfileValidator.Validate(new BrandProfile()));

    [Theory]
    [InlineData("", "Iron Club", nameof(BrandProfile.ProductName))]
    [InlineData("Iron Club Manager", "", nameof(BrandProfile.ShortName))]
    [InlineData("Iron Club Manager", "A short name that is far too long", nameof(BrandProfile.ShortName))]
    public void Names_are_required_and_bounded(string product, string shortName, string field) =>
        Assert.Contains(field, Fields(Valid() with { ProductName = product, ShortName = shortName }));

    [Theory]
    [InlineData("ironclub.example")]       // no scheme
    [InlineData("ftp://ironclub.example")] // not http(s)
    public void Website_must_be_http_url(string url) =>
        Assert.Contains(nameof(BrandProfile.WebsiteUrl), Fields(Valid() with { WebsiteUrl = url }));

    [Fact]
    public void Email_and_colour_are_checked()
    {
        Assert.Contains(nameof(BrandProfile.SupportEmail), Fields(Valid() with { SupportEmail = "not-an-email" }));
        Assert.Contains(nameof(BrandProfile.AccentColor), Fields(Valid() with { AccentColor = "red" }));
    }

    [Fact]
    public void Default_theme_must_exist_and_be_allowed()
    {
        Assert.Contains(nameof(BrandProfile.DefaultTheme), Fields(Valid() with { DefaultTheme = "no-such-theme" }));
        Assert.Contains(nameof(BrandProfile.DefaultTheme), Fields(Valid() with { DefaultTheme = "fluent-light" })); // not in allowed list
        Assert.Contains(nameof(BrandProfile.AllowedThemes), Fields(Valid() with { AllowedThemes = ["bogus"] }));
    }

    [Theory]
    [InlineData("Iron Club", true)]
    [InlineData("Iron-Club_2", true)]
    [InlineData("نادي الأبطال", true)]     // Arabic folder names are fine on Windows
    [InlineData("..\\Windows", false)]     // path traversal
    [InlineData("Iron/Club", false)]
    [InlineData("Iron Club.", false)]      // Windows strips trailing dots
    [InlineData("CON", false)]             // reserved device name
    [InlineData("", false)]
    public void Data_folder_name_must_be_a_plain_folder(string name, bool ok) =>
        Assert.Equal(ok, !Fields(Valid() with { DataFolderName = name }).Contains(nameof(BrandProfile.DataFolderName)));

    [Theory]
    [InlineData("IronClub", true)]
    [InlineData("Iron.Club-2", true)]
    [InlineData("Iron Club", false)]   // no spaces in exe names
    [InlineData("IronClub.exe", false)]
    [InlineData("NUL", false)]
    public void Exe_name_must_be_a_plain_file_name(string name, bool ok) =>
        Assert.Equal(ok, !Fields(Valid() with { ExeName = name }).Contains(nameof(BrandProfile.ExeName)));

    [Fact]
    public void Brands_from_a_newer_version_are_refused() =>
        Assert.Contains(nameof(BrandProfile.SchemaVersion), Fields(Valid() with { SchemaVersion = 99 }));
}

public class BrandJsonAndOptionsTests
{
    [Fact]
    public void Json_round_trip_keeps_every_field_including_arabic()
    {
        var p = new BrandProfile
        {
            ProductName = "نادي الأبطال", ShortName = "الأبطال", CompanyName = "Champions LLC", Copyright = "(c) 2026 Champions",
            Description = "Gym system", WebsiteUrl = "https://champions.example", SupportEmail = "a@b.example", SupportPhone = "+20 100 000 0000",
            LoginWelcome = "Welcome back", ShowPoweredBy = false, AccentColor = "#112233", DefaultTheme = "office-black",
            AllowedThemes = ["office-black", "office-colorful"], DataFolderName = "Champions", ExeName = "Champions",
            LogoPath = "logo.png", IconPath = "app.ico",
        };

        var json = BrandJson.Serialize(p);
        Assert.Contains("نادي الأبطال", json); // not \u-escaped
        var back = BrandJson.Deserialize(json);
        Assert.Equal(p with { AllowedThemes = [] }, back with { AllowedThemes = [] });
        Assert.Equal(p.AllowedThemes, back.AllowedThemes);
    }

    [Fact]
    public void Invalid_json_is_reported_not_crashed() =>
        Assert.Throws<InvalidDataException>(() => BrandJson.Deserialize("{ productName: "));

    [Fact]
    public void Brand_json_binds_directly_into_the_Branding_section()
    {
        // tools/publish.ps1 copies brand.json fields into appsettings "Branding" as-is; this proves that works.
        var p = new BrandProfile { ProductName = "Iron Club Manager", ShortName = "Iron Club", AllowedThemes = ["material-dark"], DefaultTheme = "material-dark", DataFolderName = "IronClub", ShowPoweredBy = false };
        var o = Bind(new JsonObject { ["Branding"] = JsonNode.Parse(BrandJson.Serialize(p)) });

        Assert.Equal("Iron Club Manager", o.EffectiveProductName);
        Assert.Equal("Iron Club", o.EffectiveShortName);
        Assert.Equal(["material-dark"], o.AllowedThemes);
        Assert.Equal("IronClub", o.EffectiveDataFolderName);
        Assert.False(o.ShowPoweredBy);
    }

    [Fact]
    public void Old_appsettings_with_only_AppTitle_still_works()
    {
        var o = Bind(JsonNode.Parse("""{ "Branding": { "AppTitle": "Champions Gym", "AccentColor": "#E53935", "DefaultTheme": "fluent-light" } }""")!.AsObject());

        Assert.Equal("Champions Gym", o.EffectiveProductName);
        Assert.Equal("Champions Gym", o.EffectiveShortName);        // falls back to the title
        Assert.Equal(BrandDefaults.DataFolderName, o.EffectiveDataFolderName); // existing installs stay in %ProgramData%\GymPro
        Assert.True(o.ShowPoweredBy);
        Assert.Empty(o.AllowedThemes);
    }

    [Fact]
    public void Writer_section_binds_back_and_keeps_legacy_title()
    {
        var p = new BrandProfile { ProductName = "Iron Club Manager", ShortName = "Iron Club", AllowedThemes = ["fluent-dark", "fluent-light"], DataFolderName = "IronClub" };
        var section = BrandingWriter.BuildSection(p, "Branding/logo.png", null, "Branding/app.ico");
        var o = Bind(new JsonObject { ["Branding"] = section });

        Assert.Equal("Iron Club Manager", o.AppTitle); // older app versions read AppTitle
        Assert.Equal("Branding/app.ico", o.IconPath);
        Assert.Equal(["fluent-dark", "fluent-light"], o.AllowedThemes);
    }

    [Fact]
    public void Database_path_uses_the_brand_data_folder()
    {
        Assert.Equal(@"%ProgramData%\GymPro\gym.db", new DatabaseOptions().PathFor(BrandDefaults.DataFolderName));
        Assert.Equal(@"%ProgramData%\IronClub\gym.db", new DatabaseOptions().PathFor("IronClub"));
        Assert.Equal(@"D:\data\gym.db", new DatabaseOptions { Path = @"D:\data\gym.db" }.PathFor("IronClub")); // explicit paths win
    }

    private static BrandingOptions Bind(JsonObject root)
    {
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(root.ToJsonString()));
        return new ConfigurationBuilder().AddJsonStream(ms).Build().GetSection(BrandingOptions.SectionName).Get<BrandingOptions>()!;
    }
}

public class BrandPackTests
{
    private static readonly byte[] FakePng = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4];

    private static BrandPackContent Sample() => new(
        new BrandProfile { ProductName = "Iron Club Manager", ShortName = "Iron Club", ExeName = "IronClub" },
        FakePng,
        IcoFile.Write([(16, FakePng), (256, FakePng)]));

    [Fact]
    public void Folder_pack_round_trips()
    {
        var dir = Directory.CreateTempSubdirectory("brand-folder-").FullName;
        BrandPack.SaveFolder(Sample(), dir);

        Assert.True(File.Exists(Path.Combine(dir, BrandPack.ProfileFile)));
        var loaded = BrandPack.Load(dir);
        Assert.Equal("Iron Club", loaded.Profile.ShortName);
        Assert.Equal(BrandPack.LogoFile, loaded.Profile.LogoPath);
        Assert.Equal(FakePng, loaded.Logo);
        Assert.Equal(2, IcoFile.Read(loaded.Icon!).Count);

        // also loadable by pointing at brand.json itself
        Assert.Equal("Iron Club", BrandPack.Load(Path.Combine(dir, BrandPack.ProfileFile)).Profile.ShortName);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Zip_pack_round_trips()
    {
        var zip = Path.Combine(Path.GetTempPath(), $"brand-{Guid.NewGuid():N}{BrandPack.Extension}");
        BrandPack.SaveZip(Sample(), zip);
        var loaded = BrandPack.Load(zip);

        Assert.Equal("Iron Club Manager", loaded.Profile.ProductName);
        Assert.Equal(FakePng, loaded.Logo);
        Assert.NotNull(loaded.Icon);
        File.Delete(zip);
    }

    [Fact]
    public void Removing_the_logo_removes_the_file_from_the_folder()
    {
        var dir = Directory.CreateTempSubdirectory("brand-folder-").FullName;
        BrandPack.SaveFolder(Sample(), dir);
        BrandPack.SaveFolder(Sample() with { Logo = null }, dir);

        Assert.False(File.Exists(Path.Combine(dir, BrandPack.LogoFile)));
        Assert.Null(BrandPack.Load(dir).Logo);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void A_brand_json_cannot_point_outside_its_pack()
    {
        var dir = Directory.CreateTempSubdirectory("brand-evil-").FullName;
        File.WriteAllText(Path.Combine(dir, BrandPack.ProfileFile), """{ "productName": "X", "shortName": "X", "logoPath": "..\\..\\secret.txt" }""");

        Assert.Throws<InvalidDataException>(() => BrandPack.Load(dir));
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Ico_writer_writes_every_standard_size()
    {
        var ico = IcoFile.Write([.. IcoFile.StandardSizes.Select(s => (s, FakePng))]);
        var entries = IcoFile.Read(ico);

        Assert.Equal(IcoFile.StandardSizes, entries.Select(e => e.Width));
        Assert.All(entries, e => Assert.Equal(FakePng.Length, e.Length));
        Assert.Equal(6 + (16 * entries.Count), entries[0].Offset); // data starts right after the directory
        Assert.Throws<InvalidDataException>(() => IcoFile.Read([1, 2, 3]));
    }
}

public class AllowedThemeTests
{
    [Fact]
    public void Empty_restriction_offers_every_theme() =>
        Assert.Equal(ThemeCatalog.All.Count, ThemeCatalog.Allowed([]).Count);

    [Fact]
    public void Saved_preference_outside_the_brand_falls_back_to_the_brand_default()
    {
        string[] allowed = ["material-light", "material-dark"];
        Assert.Equal("material-dark", ThemeCatalog.Pick("material-dark", allowed, "material-light").Id); // allowed: kept
        Assert.Equal("material-light", ThemeCatalog.Pick("office-black", allowed, "material-light").Id); // not allowed: brand default
        Assert.Equal("material-light", ThemeCatalog.Pick("office-black", allowed, "high-contrast").Id); // default not allowed: first allowed
    }

    [Fact]
    public void Unknown_ids_in_the_restriction_are_ignored() =>
        Assert.Equal(ThemeCatalog.All.Count, ThemeCatalog.Allowed(["nope"]).Count);
}
