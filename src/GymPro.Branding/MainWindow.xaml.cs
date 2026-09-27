using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GymPro.Core.Configuration;
using GymPro.Core.Theming;
using Microsoft.Win32;

namespace GymPro.Branding;

/// <summary>
/// Picks a logo + title (+ accent colour), previews them, and writes the "Branding" section
/// of the GymPro app's appsettings.json. Other sections are preserved; a .bak is kept.
/// </summary>
public partial class MainWindow : Window
{
    private const string LogoRelativePath = "Branding/logo.png";
    private const int LogoMaxPixels = 512;
    private const int EmbedWarnBytes = 200 * 1024;

    private byte[]? _logoPng; // normalised logo (PNG, max 512 px)

    public MainWindow()
    {
        InitializeComponent();
        ThemeBox.ItemsSource = ThemeCatalog.All.Select(t => new { t.Id, Label = $"{t.DisplayName}  ({t.Family})" }).ToList();
        ThemeBox.SelectedValue = ThemeCatalog.DefaultId;
        TargetBox.Text = GuessTarget() ?? "";
        Refresh();
    }

    private void OnInputChanged(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            Refresh();
        }
    }

    private void OnBrowseLogo(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.ico;*.gif" };
        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _logoPng = NormalizeToPng(dlg.FileName, out var w, out var h);
            LogoFileBox.Text = dlg.FileName;
            LogoInfo.Text = $"{w}×{h}px → saved as PNG, {_logoPng.Length / 1024.0:0.#} KB" + (w > LogoMaxPixels || h > LogoMaxPixels ? $" (scaled to fit {LogoMaxPixels}px)" : "");
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or FileFormatException)
        {
            _logoPng = null;
            LogoFileBox.Text = "";
            LogoInfo.Text = "";
            MessageBox.Show(this, "That file could not be read as an image.\n" + ex.Message, "Logo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        Refresh();
    }

    private void OnClearLogo(object sender, RoutedEventArgs e)
    {
        _logoPng = null;
        LogoFileBox.Text = LogoInfo.Text = "";
        Refresh();
    }

    private void OnBrowseTarget(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "appsettings.json|appsettings.json|JSON|*.json", CheckFileExists = true };
        if (dlg.ShowDialog(this) == true)
        {
            TargetBox.Text = dlg.FileName;
        }
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        if (!TryBuild(forTarget: true, out var section))
        {
            return;
        }

        var target = TargetBox.Text.Trim();
        try
        {
            if (CopyMode.IsChecked == true && _logoPng is not null)
            {
                var logoFile = Path.Combine(Path.GetDirectoryName(target)!, LogoRelativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(logoFile)!);
                File.WriteAllBytes(logoFile, _logoPng);
            }

            BrandingWriter.WriteToFile(target, section);
            StatusText.Text = $"Saved to {target}\n(previous version kept as appsettings.json.bak). Restart GymPro to see the new branding.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            StatusText.Text = "";
            MessageBox.Show(this, ex.Message + (ex is UnauthorizedAccessException ? "\n\nIf GymPro is installed under Program Files, run this tool as administrator." : ""),
                "Could not save", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnCopyJson(object sender, RoutedEventArgs e)
    {
        if (TryBuild(forTarget: false, out _))
        {
            Clipboard.SetText(JsonBox.Text);
            StatusText.Text = "JSON copied. Paste it into appsettings.json as the \"Branding\" section.";
        }
    }

    private void OnSaveJson(object sender, RoutedEventArgs e)
    {
        if (!TryBuild(forTarget: false, out var section))
        {
            return;
        }

        var dlg = new SaveFileDialog { FileName = "branding.json", Filter = "JSON|*.json" };
        if (dlg.ShowDialog(this) == true)
        {
            File.WriteAllText(dlg.FileName, BrandingWriter.ToJson(new JsonObject { [BrandingOptions.SectionName] = section }));
            if (CopyMode.IsChecked == true && _logoPng is not null)
            {
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(dlg.FileName)!, "logo.png"), _logoPng);
            }

            StatusText.Text = "Saved " + dlg.FileName;
        }
    }

    // ------------------------------------------------------------------ helpers

    private void Refresh()
    {
        var title = TitleBox.Text;
        PreviewTitle.Text = string.IsNullOrWhiteSpace(title) ? "(title)" : title.Trim();
        Title = "GymPro Branding Tool - " + PreviewTitle.Text;

        var brush = TryBrush(AccentBox.Text) ?? Brushes.Gray;
        AccentSwatch.Background = brush;
        PreviewHeader.Background = brush;

        var logo = LoadImage(_logoPng);
        PreviewLogo.Source = logo;
        PreviewLogo.Visibility = logo is null ? Visibility.Collapsed : Visibility.Visible;

        var errors = BrandingWriter.Validate(title, AccentBox.Text);
        ErrorText.Text = string.Join("\n", errors);
        if (EmbedMode.IsChecked == true && _logoPng is { Length: > EmbedWarnBytes })
        {
            ErrorText.Text += (errors.Count > 0 ? "\n" : "") + "Note: large embedded logos make appsettings.json hard to edit; consider 'Copy file'.";
        }

        JsonBox.Text = errors.Count == 0
            ? BrandingWriter.ToJson(new JsonObject { [BrandingOptions.SectionName] = BuildSection(truncateBase64: true) })
            : "";
        ApplyButton.IsEnabled = errors.Count == 0 && !string.IsNullOrWhiteSpace(TargetBox.Text);
    }

    private JsonObject BuildSection(bool truncateBase64 = false)
    {
        string? path = null, b64 = null;
        if (_logoPng is not null)
        {
            if (EmbedMode.IsChecked == true)
            {
                b64 = Convert.ToBase64String(_logoPng);
                if (truncateBase64 && b64.Length > 120)
                {
                    b64 = b64[..60] + $"...({b64.Length:N0} chars)...";
                }
            }
            else
            {
                path = LogoRelativePath;
            }
        }

        return BrandingWriter.BuildSection(TitleBox.Text, AccentBox.Text, path, b64, ThemeBox.SelectedValue as string);
    }

    private bool TryBuild(bool forTarget, out JsonObject section)
    {
        section = [];
        var errors = BrandingWriter.Validate(TitleBox.Text, AccentBox.Text).ToList();
        if (forTarget)
        {
            var target = TargetBox.Text.Trim();
            if (string.IsNullOrEmpty(target) || !Path.GetFileName(target).EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Choose the GymPro appsettings.json to update.");
            }
            else if (!Directory.Exists(Path.GetDirectoryName(target)))
            {
                errors.Add("The target folder does not exist.");
            }
        }

        if (errors.Count > 0)
        {
            ErrorText.Text = string.Join("\n", errors);
            return false;
        }

        section = BuildSection();
        return true;
    }

    private static byte[] NormalizeToPng(string file, out int width, out int height)
    {
        var decoder = BitmapDecoder.Create(new Uri(file), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapSource frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).First(); // .ico: take the largest size
        width = frame.PixelWidth;
        height = frame.PixelHeight;

        var scale = Math.Min(1.0, (double)LogoMaxPixels / Math.Max(width, height));
        if (scale < 1.0)
        {
            frame = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
        }

        var enc = new PngBitmapEncoder(); // PNG keeps transparency
        enc.Frames.Add(BitmapFrame.Create(frame));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    private static BitmapImage? LoadImage(byte[]? bytes)
    {
        if (bytes is null)
        {
            return null;
        }

        using var ms = new MemoryStream(bytes);
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.StreamSource = ms;
        img.EndInit();
        img.Freeze();
        return img;
    }

    private static SolidColorBrush? TryBrush(string? hex)
    {
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex?.Trim() ?? ""));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>When run from the repo, point at the sibling app's appsettings.json.</summary>
    private static string? GuessTarget()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "GymPro.App", "appsettings.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var installed = Path.Combine(dir.FullName, "GymPro.exe");
            if (File.Exists(installed) && File.Exists(Path.Combine(dir.FullName, "appsettings.json")))
            {
                return Path.Combine(dir.FullName, "appsettings.json");
            }
        }

        return null;
    }
}
