using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymPro.Core.Branding;
using GymPro.Core.Configuration;
using GymPro.Core.Theming;
using Microsoft.Win32;

namespace GymPro.BrandStudio;

/// <summary>
/// Brand Studio: create a reseller brand, preview it with the app's real theme engine, and
/// save it as a brand pack, apply it to an installed app, or build a branded package.
/// Nothing is written until the user presses an explicit button, and every write keeps a backup or goes to a new file.
/// </summary>
public sealed partial class StudioViewModel : ObservableObject
{
    // Changing any of these re-validates and refreshes the preview.
    private static readonly HashSet<string> BrandFields =
    [
        nameof(ProductName), nameof(ShortName), nameof(CompanyName), nameof(Copyright), nameof(Description),
        nameof(WebsiteUrl), nameof(SupportEmail), nameof(SupportPhone), nameof(LoginWelcome), nameof(ShowPoweredBy),
        nameof(AccentColor), nameof(DefaultTheme), nameof(DataFolderName), nameof(ExeName), nameof(LogoPng), nameof(IconBytes),
        nameof(PreviewTheme),
    ];

    private bool _loading;

    public StudioViewModel()
    {
        foreach (var t in ThemeCatalog.All)
        {
            Themes.Add(new ThemeChoice(t, OnThemesChanged));
        }

        RepoRoot = FindRepoRoot(AppContext.BaseDirectory);
        Reset(new BrandProfile { ProductName = "", ShortName = "", DataFolderName = "", ExeName = "" }, null, null, null);
    }

    public ErrorBag Errors { get; } = new();
    public ObservableCollection<ThemeChoice> Themes { get; } = [];
    public IReadOnlyList<ThemeDefinition> AllThemes { get; } = ThemeCatalog.All;
    public ObservableCollection<ThemeDefinition> PreviewThemes { get; } = [];
    public ObservableCollection<ContrastCheck> Contrast { get; } = [];
    public ObservableCollection<IconPreview> IconPreviews { get; } = [];

    // ---------------------------------------------------------------- identity
    [ObservableProperty] public partial string ProductName { get; set; } = "";
    [ObservableProperty] public partial string ShortName { get; set; } = "";
    [ObservableProperty] public partial string? CompanyName { get; set; }
    [ObservableProperty] public partial string? Copyright { get; set; }
    [ObservableProperty] public partial string? Description { get; set; }
    [ObservableProperty] public partial string? WebsiteUrl { get; set; }
    [ObservableProperty] public partial string? SupportEmail { get; set; }
    [ObservableProperty] public partial string? SupportPhone { get; set; }
    [ObservableProperty] public partial string? LoginWelcome { get; set; }
    [ObservableProperty] public partial bool ShowPoweredBy { get; set; } = true;

    // ---------------------------------------------------------------- look
    [ObservableProperty] public partial string AccentColor { get; set; } = BrandDefaults.AccentColor;
    [ObservableProperty] public partial string DefaultTheme { get; set; } = ThemeCatalog.DefaultId;
    [ObservableProperty] public partial ThemeDefinition? PreviewTheme { get; set; }
    [ObservableProperty] public partial PreviewPalette? Palette { get; set; }
    [ObservableProperty] public partial string? AccentNote { get; set; }

    // ---------------------------------------------------------------- logo & icon
    [ObservableProperty] public partial byte[]? LogoPng { get; set; }
    [ObservableProperty] public partial byte[]? IconBytes { get; set; }
    [ObservableProperty] public partial ImageSource? LogoImage { get; set; }
    [ObservableProperty] public partial string? LogoInfo { get; set; }
    [ObservableProperty] public partial string? IconWarning { get; set; }

    /// <summary>True when the icon came from an .ico file (or an existing pack) instead of being generated from the logo.</summary>
    [ObservableProperty] public partial bool IconImported { get; set; }

    // ---------------------------------------------------------------- files
    [ObservableProperty] public partial string DataFolderName { get; set; } = "";
    [ObservableProperty] public partial string ExeName { get; set; } = "";

    // ---------------------------------------------------------------- output
    [ObservableProperty] public partial string? CurrentPack { get; set; }
    [ObservableProperty] public partial bool IsDirty { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = "";
    [ObservableProperty] public partial string BuildLog { get; set; } = "";
    [ObservableProperty] public partial bool IsBuilding { get; set; }
    [ObservableProperty] public partial string? BuiltFolder { get; set; }
    [ObservableProperty] public partial string? RepoRoot { get; set; }

    public bool CanBuild => RepoRoot is not null;

    public string WindowTitle => "Brand Studio" + (CurrentPack is null ? "" : " - " + Path.GetFileName(CurrentPack.TrimEnd('\\'))) + (IsDirty ? " *" : "");

    public string PreviewProductName => string.IsNullOrWhiteSpace(ProductName) ? "(product name)" : ProductName.Trim();

    public string PreviewWelcome => string.IsNullOrWhiteSpace(LoginWelcome) ? "Sign in to continue" : LoginWelcome.Trim();

    // ================================================================ change tracking

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(CurrentPack) or nameof(IsDirty))
        {
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(WindowTitle))); // also while a pack is loading
        }

        if (_loading || e.PropertyName is null)
        {
            return;
        }

        if (BrandFields.Contains(e.PropertyName))
        {
            if (e.PropertyName != nameof(PreviewTheme))
            {
                IsDirty = true;
            }

            Refresh();
        }

        if (e.PropertyName == nameof(RepoRoot))
        {
            OnPropertyChanged(nameof(CanBuild));
        }
    }

    private void OnThemesChanged()
    {
        if (_loading)
        {
            return;
        }

        IsDirty = true;
        Refresh();
    }

    /// <summary>Re-validates and rebuilds every preview from the current fields.</summary>
    private void Refresh()
    {
        var profile = ToProfile();
        var errors = BrandProfileValidator.Validate(profile).ToList();
        if (LogoPng is null)
        {
            errors.Add(("Logo", "Add a logo: branded builds need one (it is also the icon source)."));
        }

        Errors.Set(errors);

        // Preview list follows the allowed themes; keep the current preview theme if still offered.
        var offered = ThemeCatalog.Allowed(profile.AllowedThemes);
        if (!PreviewThemes.Select(t => t.Id).SequenceEqual(offered.Select(t => t.Id)))
        {
            var keep = PreviewTheme?.Id;
            _loading = true;
            PreviewThemes.Clear();
            foreach (var t in offered)
            {
                PreviewThemes.Add(t);
            }

            PreviewTheme = offered.FirstOrDefault(t => t.Id == keep) ?? ThemeCatalog.Pick(DefaultTheme, profile.AllowedThemes, DefaultTheme);
            _loading = false;
        }

        var theme = PreviewTheme ?? ThemeCatalog.Find(DefaultTheme);
        var accent = Rgba.TryParse(AccentColor, out _) ? AccentColor : null;
        Palette = new PreviewPalette(theme, accent);
        BuildContrastReport(theme, accent);

        OnPropertyChanged(nameof(PreviewProductName));
        OnPropertyChanged(nameof(PreviewWelcome));
        SaveFolderCommand.NotifyCanExecuteChanged();
        SaveZipCommand.NotifyCanExecuteChanged();
        ApplyToAppCommand.NotifyCanExecuteChanged();
        BuildCommand.NotifyCanExecuteChanged();
    }

    private void BuildContrastReport(ThemeDefinition theme, string? accent)
    {
        var t = Palette!.Tokens;
        Contrast.Clear();
        Contrast.Add(new ContrastCheck("Button text on your colour", ColorMath.Contrast(t["OnAccent"], t["Accent"]), 4.5));
        Contrast.Add(new ContrastCheck("Header text", ColorMath.Contrast(t["OnHeader"], t["Header"]), 4.5));
        Contrast.Add(new ContrastCheck("Body text on page", ColorMath.Contrast(t["Text"], t["Surface"]), 4.5));
        Contrast.Add(new ContrastCheck("Focus ring against page", ColorMath.Contrast(t["InputFocus"], t["Surface"]), 3.0));
        Contrast.Add(new ContrastCheck("Selected menu item", ColorMath.Contrast(t["OnNavSelected"], ColorMath.Over(t["NavSelected"], t["Nav"])), 4.5));

        AccentNote = !theme.AllowBrandAccent
            ? $"{theme.DisplayName} ignores brand colours on purpose (accessibility)."
            : accent is not null && t["AccentStrong"] != t["Accent"]
                ? $"Your colour is too light to see as a focus ring on this background, so the app automatically uses a darker shade ({t["AccentStrong"]}) for focus rings and indicators. Buttons keep your colour."
                : null;
    }

    // ================================================================ profile <-> fields

    public BrandProfile ToProfile()
    {
        var allowed = Themes.Where(t => t.IsAllowed).Select(t => t.Id).ToList();
        if (allowed.Count == Themes.Count)
        {
            allowed.Clear(); // all ticked = no restriction (also future themes)
        }

        return new BrandProfile
        {
            ProductName = ProductName.Trim(),
            ShortName = ShortName.Trim(),
            CompanyName = Clean(CompanyName),
            Copyright = Clean(Copyright),
            Description = Clean(Description),
            WebsiteUrl = Clean(WebsiteUrl),
            SupportEmail = Clean(SupportEmail),
            SupportPhone = Clean(SupportPhone),
            LoginWelcome = Clean(LoginWelcome),
            ShowPoweredBy = ShowPoweredBy,
            AccentColor = AccentColor.Trim(),
            DefaultTheme = DefaultTheme,
            AllowedThemes = allowed,
            DataFolderName = DataFolderName.Trim(),
            ExeName = ExeName.Trim(),
        };

        static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }

    private void Reset(BrandProfile p, byte[]? logo, byte[]? icon, string? packPath)
    {
        _loading = true;
        ProductName = p.ProductName;
        ShortName = p.ShortName;
        CompanyName = p.CompanyName;
        Copyright = p.Copyright;
        Description = p.Description;
        WebsiteUrl = p.WebsiteUrl;
        SupportEmail = p.SupportEmail;
        SupportPhone = p.SupportPhone;
        LoginWelcome = p.LoginWelcome;
        ShowPoweredBy = p.ShowPoweredBy;
        AccentColor = p.AccentColor;
        DefaultTheme = ThemeCatalog.Find(p.DefaultTheme).Id;
        DataFolderName = p.DataFolderName;
        ExeName = p.ExeName;
        var allowed = p.AllowedThemes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var t in Themes)
        {
            t.IsAllowed = allowed.Count == 0 || allowed.Contains(t.Id);
        }

        SetLogo(logo, regenerateIcon: icon is null);
        if (icon is not null)
        {
            SetIcon(icon, imported: true);
        }

        CurrentPack = packPath;
        PreviewTheme = null;
        _loading = false;
        IsDirty = false;
        Refresh();
    }

    // ================================================================ logo & icon

    private void SetLogo(byte[]? png, bool regenerateIcon)
    {
        LogoPng = png;
        if (png is null)
        {
            LogoImage = null;
            LogoInfo = null;
            if (!IconImported)
            {
                SetIcon(null, imported: false);
            }

            return;
        }

        var img = BrandImaging.Decode(png);
        LogoImage = img;
        LogoInfo = $"{img.PixelWidth}×{img.PixelHeight} px PNG, {png.Length / 1024.0:0.#} KB";
        if (regenerateIcon)
        {
            SetIcon(BrandImaging.CreateIcon(png), imported: false);
        }
    }

    private void SetIcon(byte[]? ico, bool imported)
    {
        IconBytes = ico;
        IconImported = imported && ico is not null;
        IconPreviews.Clear();
        IconWarning = null;
        if (ico is null)
        {
            return;
        }

        var frames = BrandImaging.IconFrames(ico);
        foreach (var size in new[] { 16, 24, 32, 48, 256 })
        {
            var frame = frames.FirstOrDefault(f => f.Size == size).Image
                ?? BrandImaging.RenderSquare(frames[^1].Image, size); // .ico without this size: Windows scales the largest
            IconPreviews.Add(new IconPreview(size, frame));
        }

        IconWarning = BrandImaging.LegibilityWarning((LogoImage as BitmapSource) ?? frames[^1].Image, IconPreviews[0].Image);
    }

    [RelayCommand]
    private void PickLogo()
    {
        var dlg = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif", Title = "Choose the brand logo" };
        if (dlg.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var png = BrandImaging.NormalizeLogo(File.ReadAllBytes(dlg.FileName), out var w, out var h);
            SetLogo(png, regenerateIcon: !IconImported);
            Status = w > BrandImaging.MaxLogoPixels || h > BrandImaging.MaxLogoPixels
                ? $"Logo scaled from {w}×{h} to fit {BrandImaging.MaxLogoPixels} px."
                : "Logo added.";
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or FileFormatException or ArgumentException)
        {
            MessageBox.Show("That file couldn't be read as an image.\n" + ex.Message, "Brand Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void RemoveLogo() => SetLogo(null, regenerateIcon: false);

    [RelayCommand]
    private void ImportIcon()
    {
        var dlg = new OpenFileDialog { Filter = "Windows icon|*.ico", Title = "Use an existing icon" };
        if (dlg.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var bytes = File.ReadAllBytes(dlg.FileName);
            _ = IcoFile.Read(bytes); // validates the container
            SetIcon(bytes, imported: true);
            Status = "Icon imported. It's used as-is instead of being generated from the logo.";
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or IOException or FileFormatException)
        {
            MessageBox.Show("That file isn't a usable .ico.\n" + ex.Message, "Brand Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void RegenerateIcon()
    {
        if (LogoPng is not null)
        {
            SetIcon(BrandImaging.CreateIcon(LogoPng), imported: false);
            Status = "Icon generated from the logo (16 to 256 px).";
        }
    }

    [RelayCommand]
    private void SetAccent(string hex) => AccentColor = hex;

    // ================================================================ open / save

    [RelayCommand]
    private void New()
    {
        if (ConfirmDiscard())
        {
            Reset(new BrandProfile { ProductName = "", ShortName = "", DataFolderName = "", ExeName = "" }, null, null, null);
            Status = "New brand. Start with Identity, then add a logo.";
        }
    }

    [RelayCommand]
    private void Open()
    {
        if (!ConfirmDiscard())
        {
            return;
        }

        var dlg = new OpenFileDialog { Filter = $"Brand pack|{BrandPack.ProfileFile};*{BrandPack.Extension}", Title = "Open a brand pack (brand.json in a folder, or a .brandpack file)" };
        if (dlg.ShowDialog() != true)
        {
            return;
        }

        OpenPath(dlg.FileName);
    }

    /// <summary>Opens a pack folder, brand.json or .brandpack (also used for a path passed on the command line).</summary>
    public void OpenPath(string fileOrFolder)
    {
        try
        {
            var c = BrandPack.Load(fileOrFolder);
            var path = Path.GetFileName(fileOrFolder).Equals(BrandPack.ProfileFile, StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(Path.GetFullPath(fileOrFolder))!
                : Path.GetFullPath(fileOrFolder);
            Reset(c.Profile, c.Logo, c.Icon, path);
            Status = $"Opened {path}.";
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(ex.Message, "Couldn't open the brand pack", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private bool CanSave() => !Errors.Any;

    /// <summary>Saves as a folder: diffable and easy to keep in source control.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void SaveFolder()
    {
        var dlg = new OpenFolderDialog { Title = "Choose (or create) a folder for the brand pack" };
        if (dlg.ShowDialog() != true)
        {
            return;
        }

        var folder = dlg.FolderName;
        var existing = Path.Combine(folder, BrandPack.ProfileFile);
        if (File.Exists(existing) && !string.Equals(folder, CurrentPack, StringComparison.OrdinalIgnoreCase) &&
            MessageBox.Show($"{folder} already contains a brand pack. Replace it?", "Brand Studio", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        Write(() => BrandPack.SaveFolder(Content(), folder), folder);
    }

    /// <summary>Saves as a single .brandpack file: easy to e-mail to whoever builds the package.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void SaveZip()
    {
        var dlg = new SaveFileDialog
        {
            Filter = $"Brand pack|*{BrandPack.Extension}",
            FileName = (string.IsNullOrWhiteSpace(ExeName) ? "brand" : ExeName) + BrandPack.Extension,
        };
        if (dlg.ShowDialog() == true)
        {
            Write(() => BrandPack.SaveZip(Content(), dlg.FileName), dlg.FileName);
        }
    }

    private BrandPackContent Content() => new(ToProfile(), LogoPng, IconBytes);

    private void Write(Action save, string path)
    {
        try
        {
            save();
            CurrentPack = path;
            IsDirty = false;
            Status = $"Saved {path}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(ex.Message, "Couldn't save", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private bool ConfirmDiscard() =>
        !IsDirty || MessageBox.Show("Discard the unsaved changes to this brand?", "Brand Studio", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public bool ConfirmClose() => ConfirmDiscard();

    // ================================================================ apply to an installed app

    /// <summary>
    /// Writes the brand into an installed app's appsettings.json (keeping appsettings.json.bak) and copies the
    /// logo/icon next to it. Names, logo, colours, support details and themes change on the next start;
    /// the exe name, file properties and taskbar icon need a branded build.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void ApplyToApp()
    {
        var dlg = new OpenFileDialog { Filter = "appsettings.json|appsettings.json", Title = "Choose the installed app's appsettings.json" };
        if (dlg.ShowDialog() != true)
        {
            return;
        }

        var target = dlg.FileName;
        var appDir = Path.GetDirectoryName(target)!;
        var profile = ToProfile();
        var dataNote = DataFolderNote(target, profile.DataFolderName);
        if (MessageBox.Show(
                $"Apply \"{profile.ProductName}\" to:\n{appDir}\n\nThe current settings are kept as appsettings.json.bak.{dataNote}\n\nRestart the app afterwards.",
                "Apply brand", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            var brandDir = Path.Combine(appDir, "Branding");
            Directory.CreateDirectory(brandDir);
            if (LogoPng is not null)
            {
                File.WriteAllBytes(Path.Combine(brandDir, BrandPack.LogoFile), LogoPng);
            }

            if (IconBytes is not null)
            {
                File.WriteAllBytes(Path.Combine(brandDir, BrandPack.IconFile), IconBytes);
            }

            BrandingWriter.WriteToFile(target, BrandingWriter.BuildSection(
                profile,
                LogoPng is null ? null : $"Branding/{BrandPack.LogoFile}",
                null,
                IconBytes is null ? null : $"Branding/{BrandPack.IconFile}"));
            Status = $"Applied to {appDir}. Restart the app to see it. (Exe name, file properties and taskbar icon need a branded build.)";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(ex.Message + (ex is UnauthorizedAccessException ? "\n\nIf the app is under Program Files, run Brand Studio as administrator." : ""),
                "Couldn't apply the brand", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>A warning when the new data folder differs from what the installed app uses now.</summary>
    private static string DataFolderNote(string appSettings, string newFolder)
    {
        try
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(appSettings), documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true });
            // Keys may be PascalCase (Brand Studio, branding tool) or camelCase (publish.ps1): look up case-insensitively.
            var section = (root as System.Text.Json.Nodes.JsonObject)?.FirstOrDefault(p => p.Key.Equals(BrandingOptions.SectionName, StringComparison.OrdinalIgnoreCase)).Value as System.Text.Json.Nodes.JsonObject;
            var current = section?.FirstOrDefault(p => p.Key.Equals(nameof(BrandingOptions.DataFolderName), StringComparison.OrdinalIgnoreCase)).Value?.GetValue<string>()
                ?? BrandDefaults.DataFolderName;
            return string.Equals(current, newFolder, StringComparison.OrdinalIgnoreCase)
                ? ""
                : $"\n\nData folder changes from \"{current}\" to \"{newFolder}\". If this PC already has data in the old folder, the app will explain and stop until the database is moved; it never starts empty by mistake.";
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return "";
        }
    }

    // ================================================================ build a branded package

    private bool CanBuildNow() => CanSave() && CanBuild && !IsBuilding;

    /// <summary>Runs tools/publish.ps1 -Brand from the source checkout and streams its output.</summary>
    [RelayCommand(CanExecute = nameof(CanBuildNow))]
    private async Task BuildAsync()
    {
        var tempPack = Path.Combine(Path.GetTempPath(), "brandstudio-" + Guid.NewGuid().ToString("N"));
        BrandPack.SaveFolder(Content(), tempPack);
        BuildLog = "";
        BuiltFolder = null;
        IsBuilding = true;
        BuildCommand.NotifyCanExecuteChanged();
        try
        {
            var psi = new ProcessStartInfo("pwsh")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = RepoRoot!,
            };
            foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(RepoRoot!, "tools", "publish.ps1"), "-Brand", tempPack, "-SkipTests" })
            {
                psi.ArgumentList.Add(a);
            }

            psi.Environment["NO_COLOR"] = "1";
            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.OutputDataReceived += (_, e) => Append(e.Data);
            proc.ErrorDataReceived += (_, e) => Append(e.Data);
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            await proc.WaitForExitAsync();

            if (proc.ExitCode == 0)
            {
                BuiltFolder = Path.Combine(RepoRoot!, "publish", ExeName.Trim());
                Status = $"Built {ExeName.Trim()}.exe and its zip in {Path.Combine(RepoRoot!, "publish")}.";
            }
            else
            {
                Status = "The build failed. See the log below.";
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Status = "PowerShell 7 (pwsh) is needed to build packages: https://aka.ms/powershell";
        }
        finally
        {
            IsBuilding = false;
            BuildCommand.NotifyCanExecuteChanged();
            try
            {
                Directory.Delete(tempPack, true);
            }
            catch (IOException)
            {
            }
        }

        void Append(string? line)
        {
            if (line is not null)
            {
                Application.Current.Dispatcher.Invoke(() => BuildLog += line + Environment.NewLine);
            }
        }
    }

    [RelayCommand]
    private void OpenBuiltFolder()
    {
        if (BuiltFolder is not null && Directory.Exists(BuiltFolder))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{BuiltFolder}\"") { UseShellExecute = true });
        }
    }

    [RelayCommand]
    private void LocateSource()
    {
        var dlg = new OpenFolderDialog { Title = "Choose the source folder (the one containing GymPro.slnx)" };
        if (dlg.ShowDialog() == true)
        {
            RepoRoot = FindRepoRoot(dlg.FolderName);
            Status = RepoRoot is null ? "That folder isn't a source checkout (no GymPro.slnx and tools\\publish.ps1)." : $"Source folder: {RepoRoot}";
            BuildCommand.NotifyCanExecuteChanged();
        }
    }

    private static string? FindRepoRoot(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GymPro.slnx")) && File.Exists(Path.Combine(dir.FullName, "tools", "publish.ps1")))
            {
                return dir.FullName;
            }
        }

        return null;
    }
}

public sealed record IconPreview(int Size, BitmapSource Image)
{
    public string Label => $"{Size} px";
}
