using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GymPro.Core.Configuration;
using Microsoft.Extensions.Options;

namespace GymPro.App.Infrastructure;

/// <summary>
/// The product identity the user sees: names, logo, icon, support contacts and data folder, resolved from the
/// <c>Branding</c> section. White-label builds change all of it; nothing in the UI may hard-code the product name.
/// </summary>
public sealed class Branding
{
    public Branding(IOptions<BrandingOptions> options)
        : this(options.Value)
    {
    }

    public Branding(BrandingOptions o)
    {
        ProductName = o.EffectiveProductName;
        ShortName = o.EffectiveShortName;
        CompanyName = Blank(o.CompanyName);
        Copyright = Blank(o.Copyright);
        Description = Blank(o.Description);
        WebsiteUrl = Blank(o.WebsiteUrl);
        SupportEmail = Blank(o.SupportEmail);
        SupportPhone = Blank(o.SupportPhone);
        LoginWelcome = Blank(o.LoginWelcome);
        ShowPoweredBy = o.ShowPoweredBy;
        DataFolderName = o.EffectiveDataFolderName;
        DefaultTheme = o.DefaultTheme;
        AllowedThemes = o.AllowedThemes ?? [];
        AccentHex = o.AccentColor;
        Accent = ParseColor(o.AccentColor);
        Logo = LoadLogo(o);
        Icon = LoadIcon(o.IconPath);
    }

    /// <summary>
    /// The brand for code that runs outside DI (start-up errors, crash log, dialogs, per-PC settings).
    /// Set as soon as appsettings.json is read; until then the plain product defaults apply.
    /// </summary>
    public static Branding Current { get; set; } = new(new BrandingOptions());

    public string ProductName { get; }
    public string ShortName { get; }

    /// <summary>Kept for existing bindings: the window/header title is the product name.</summary>
    public string Title => ProductName;

    public string? CompanyName { get; }
    public string? Copyright { get; }
    public string? Description { get; }
    public string? WebsiteUrl { get; }
    public string? SupportEmail { get; }
    public string? SupportPhone { get; }
    public string? LoginWelcome { get; }
    public bool ShowPoweredBy { get; }
    public string DataFolderName { get; }
    public string DefaultTheme { get; }
    public IReadOnlyList<string> AllowedThemes { get; }
    public string AccentHex { get; }
    public Color Accent { get; }

    /// <summary>The brand's own logo, if any.</summary>
    public ImageSource? Logo { get; }

    /// <summary>The brand's .ico, if any (window and taskbar icon at runtime).</summary>
    public ImageSource? Icon { get; }

    /// <summary>Icon for windows: the brand icon, else its logo, else null (= the icon built into the exe).</summary>
    public ImageSource? WindowIcon => Icon ?? Logo;

    /// <summary>Used wherever a member has no photo: the brand logo, or the built-in app logo.</summary>
    public ImageSource PlaceholderLogo => Logo ?? AppLogo;

    public static ImageSource AppLogo { get; } = LoadAppLogo();

    /// <summary>"Contact support: help@x.com · +20 ..." or null when the brand has no support details.</summary>
    public string? SupportLine
    {
        get
        {
            var parts = new[] { SupportEmail, SupportPhone, WebsiteUrl }.Where(p => p is not null).ToList();
            return parts.Count == 0 ? null : "Contact support: " + string.Join(" · ", parts);
        }
    }

    /// <summary>%LOCALAPPDATA%\&lt;data folder&gt;: logs and per-PC UI settings.</summary>
    public string LocalDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DataFolderName);

    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static BitmapImage LoadAppLogo()
    {
        var img = new BitmapImage();
        img.BeginInit();
        img.UriSource = new Uri("pack://application:,,,/Assets/app-logo.png");
        img.DecodePixelWidth = 256;
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.EndInit();
        img.Freeze();
        return img;
    }

    private static string ResolvePath(string path)
    {
        path = Environment.ExpandEnvironmentVariables(path);
        return Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
    }

    private static ImageSource? LoadLogo(BrandingOptions o)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(o.LogoBase64))
            {
                return Images.FromBytes(Convert.FromBase64String(o.LogoBase64));
            }

            if (!string.IsNullOrWhiteSpace(o.LogoPath))
            {
                var path = ResolvePath(o.LogoPath);
                return File.Exists(path) ? Images.FromBytes(File.ReadAllBytes(path)) : null;
            }
        }
        catch (Exception ex) when (ex is FormatException or IOException or NotSupportedException or ArgumentException)
        {
            // A broken logo must never stop the gym from opening; fall back to text-only branding.
        }

        return null;
    }

    /// <summary>Loads the largest frame of an .ico (or any image) for window icons.</summary>
    private static BitmapSource? LoadIcon(string? iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            return null;
        }

        try
        {
            var path = ResolvePath(iconPath);
            if (!File.Exists(path))
            {
                return null;
            }

            using var ms = new MemoryStream(File.ReadAllBytes(path));
            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
            frame.Freeze();
            return frame;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or FileFormatException
            or System.Runtime.InteropServices.COMException)
        {
            return null; // fall back to the logo / built-in exe icon
        }
    }

    private static Color ParseColor(string? value)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(value ?? GymPro.Core.Branding.BrandDefaults.AccentColor);
        }
        catch (FormatException)
        {
            return Color.FromRgb(0x1E, 0x88, 0xE5);
        }
    }
}

public static class Images
{
    public static BitmapImage? FromBytes(byte[]? bytes, int decodeWidth = 0)
    {
        if (bytes is not { Length: > 0 })
        {
            return null;
        }

        var img = new BitmapImage();
        using var ms = new MemoryStream(bytes);
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        if (decodeWidth > 0)
        {
            img.DecodePixelWidth = decodeWidth;
        }

        img.StreamSource = ms;
        img.EndInit();
        img.Freeze();
        return img;
    }

    /// <summary>Downscale a picked photo file to the stored photo size and re-encode as JPEG, so the DB stays small.</summary>
    public static byte[] NormalizePhoto(string file) =>
        ResizeJpeg(File.ReadAllBytes(file), GymPro.Data.Services.PhotoSizes.Photo, GymPro.Data.Services.PhotoSizes.PhotoQuality);

    /// <summary>
    /// Re-encodes <paramref name="image"/> as a JPEG that fits in <paramref name="maxPixels"/> (never upscales).
    /// The size is read from the header first so the decoder can scale while decoding: fast even for huge photos.
    /// Thread-safe: everything created here is frozen and local.
    /// </summary>
    public static byte[] ResizeJpeg(byte[] image, int maxPixels, int quality)
    {
        int width, height;
        using (var probe = new MemoryStream(image))
        {
            var frame = BitmapDecoder.Create(probe, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            width = frame.PixelWidth;
            height = frame.PixelHeight;
        }

        var img = new BitmapImage();
        using (var ms = new MemoryStream(image))
        {
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            if (Math.Max(width, height) > maxPixels)
            {
                if (width >= height)
                {
                    img.DecodePixelWidth = maxPixels;
                }
                else
                {
                    img.DecodePixelHeight = maxPixels;
                }
            }

            img.StreamSource = ms;
            img.EndInit();
        }

        img.Freeze();
        return EncodeJpeg(img, quality);
    }

    public static byte[] EncodeJpeg(BitmapSource source, int quality)
    {
        var enc = new JpegBitmapEncoder { QualityLevel = quality };
        enc.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }
}

/// <summary>WPF implementation of the data layer's image hook (thumbnails, photo shrinking).</summary>
public sealed class WpfImageProcessor : GymPro.Data.Services.IImageProcessor
{
    public byte[]? ResizeJpeg(byte[] image, int maxPixels, int quality)
    {
        try
        {
            return Images.ResizeJpeg(image, maxPixels, quality);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or OverflowException
            or IOException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return null; // not a decodable image; callers keep the original
        }
    }
}
