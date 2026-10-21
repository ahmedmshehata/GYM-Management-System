using System.Text.RegularExpressions;
using GymPro.Core.Configuration;
using GymPro.Data;

namespace GymPro.Tests;

public partial class WhiteLabelGuardTests
{
    /// <summary>
    /// White-label builds must show no "GymPro" to end users. Any user-visible string literal in the app
    /// (C# strings, XAML text/attributes) containing it fails here; use Branding.Current / BrandDefaults instead.
    /// Code identifiers (namespaces, x:Class, clr-namespace, qualified type names, pack URIs) are fine.
    /// </summary>
    [Fact]
    public void App_has_no_hard_coded_product_name_in_user_visible_text()
    {
        var app = Path.Combine(RepoRoot(), "src", "GymPro.App");
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories).Where(NotGenerated))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimStart();
                if (line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith("///", StringComparison.Ordinal))
                {
                    continue; // comments aren't shown to users
                }

                foreach (Match m in CsString().Matches(lines[i]))
                {
                    var text = m.Groups[1].Value;
                    if (text.Contains("GymPro", StringComparison.Ordinal) && !IsCodeReference(text))
                    {
                        offenders.Add($"{Rel(file)}:{i + 1}: \"{text}\"");
                    }
                }
            }
        }

        foreach (var file in Directory.EnumerateFiles(app, "*.xaml", SearchOption.AllDirectories).Where(NotGenerated))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var visible = XamlNamespaceAttrs().Replace(lines[i], ""); // drop x:Class / xmlns / clr-namespace
                if (visible.Contains("GymPro", StringComparison.Ordinal) && !visible.TrimStart().StartsWith("<!--", StringComparison.Ordinal))
                {
                    offenders.Add($"{Rel(file)}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Hard-coded product name in user-visible text (use Branding.Current.ProductName / ShortName):\n" + string.Join("\n", offenders));

        static bool NotGenerated(string f) =>
            !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
            !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

        string Rel(string f) => Path.GetRelativePath(RepoRoot(), f);
    }

    // "...": regular or interpolated string contents on one line
    [GeneratedRegex(@"\$?""((?:[^""\\]|\\.)*)""")]
    private static partial Regex CsString();

    [GeneratedRegex(@"(x:Class|xmlns(:\w+)?)=""[^""]*""")]
    private static partial Regex XamlNamespaceAttrs();

    // "GymPro.App.Views", "pack://...": references to code/resources, not text a user reads
    private static bool IsCodeReference(string s) =>
        s.StartsWith("pack://", StringComparison.Ordinal) || Regex.IsMatch(s, @"^GymPro(\.[A-Z]\w*)+$");

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GymPro.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root (GymPro.slnx) not found.");
    }

    [Fact]
    public void Moving_the_data_folder_never_hides_existing_data()
    {
        var programData = Directory.CreateTempSubdirectory("pd-").FullName;
        var configured = new DatabaseOptions { Path = Path.Combine(programData, "{DataFolder}", "gym.db") };
        Directory.CreateDirectory(Path.Combine(programData, "GymPro"));
        File.WriteAllText(Path.Combine(programData, "GymPro", "gym.db"), "existing");

        // Brand moved the folder, new folder empty, old one has data -> stop and explain.
        var problem = DataServiceCollectionExtensions.FindDataLeftBehind(configured, "IronClub", "GymPro");
        Assert.NotNull(problem);
        Assert.EndsWith("IronClub", problem.Value.NewFolder);
        Assert.EndsWith("GymPro", problem.Value.ExistingFolder);

        // Default folder, explicit path, or data already moved -> fine.
        Assert.Null(DataServiceCollectionExtensions.FindDataLeftBehind(configured, "GymPro", "GymPro"));
        Assert.Null(DataServiceCollectionExtensions.FindDataLeftBehind(new DatabaseOptions { Path = Path.Combine(programData, "x.db") }, "IronClub", "GymPro"));
        Directory.CreateDirectory(Path.Combine(programData, "IronClub"));
        File.WriteAllText(Path.Combine(programData, "IronClub", "gym.db"), "moved");
        Assert.Null(DataServiceCollectionExtensions.FindDataLeftBehind(configured, "IronClub", "GymPro"));

        Directory.Delete(programData, true);
    }
}
