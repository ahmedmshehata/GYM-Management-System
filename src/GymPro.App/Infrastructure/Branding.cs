using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GymPro.Core.Configuration;
using Microsoft.Extensions.Options;

namespace GymPro.App.Infrastructure;

/// <summary>Resolves the <c>Branding</c> section of appsettings.json into things WPF can bind to.</summary>
public sealed class Branding
{
    public Branding(IOptions<BrandingOptions> options)
    {
        var o = options.Value;
        Title = string.IsNullOrWhiteSpace(o.AppTitle) ? "GymPro" : o.AppTitle.Trim();
        Logo = LoadLogo(o);
        Accent = ParseColor(o.AccentColor);
    }

    public string Title { get; }

    /// <summary>The gym's own logo from appsettings.json, if any.</summary>
    public ImageSource? Logo { get; }

    /// <summary>The gym logo, or the GymPro dumbbell: used wherever a member has no photo.</summary>
    public ImageSource PlaceholderLogo => Logo ?? AppLogo;

    public static ImageSource AppLogo { get; } = LoadAppLogo();

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
    public Color Accent { get; }

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
                var path = Environment.ExpandEnvironmentVariables(o.LogoPath);
                path = Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
                return File.Exists(path) ? Images.FromBytes(File.ReadAllBytes(path)) : null;
            }
        }
        catch (Exception ex) when (ex is FormatException or IOException or NotSupportedException)
        {
            // A broken logo must never stop the gym from opening; fall back to text-only branding.
        }

        return null;
    }

    private static Color ParseColor(string? value)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(value ?? "#1E88E5");
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
