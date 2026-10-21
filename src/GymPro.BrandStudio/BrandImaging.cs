using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GymPro.Core.Branding;

namespace GymPro.BrandStudio;

/// <summary>Logo normalising, icon generation and preview decoding (WPF imaging, no extra libraries).</summary>
public static class BrandImaging
{
    public const int MaxLogoPixels = 512;

    public static BitmapSource Decode(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapSource frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).First(); // .ico: largest size
        frame.Freeze();
        return frame;
    }

    /// <summary>Fits the logo in <see cref="MaxLogoPixels"/> px and saves it as PNG (keeps transparency).</summary>
    public static byte[] NormalizeLogo(byte[] input, out int width, out int height)
    {
        var src = Decode(input);
        width = src.PixelWidth;
        height = src.PixelHeight;
        var scale = Math.Min(1.0, (double)MaxLogoPixels / Math.Max(width, height));
        BitmapSource result = scale < 1.0 ? new TransformedBitmap(src, new ScaleTransform(scale, scale)) : src;
        return EncodePng(result);
    }

    /// <summary>A Windows icon (16 to 256 px) made from the logo, each size rendered separately for sharpness.</summary>
    public static byte[] CreateIcon(byte[] logoPng)
    {
        var logo = Decode(logoPng);
        return IcoFile.Write([.. IcoFile.StandardSizes.Select(s => (s, EncodePng(RenderSquare(logo, s))))]);
    }

    /// <summary>Draws the image centred in a transparent square, scaled to fit with a small margin.</summary>
    public static BitmapSource RenderSquare(BitmapSource image, int size)
    {
        var margin = size <= 24 ? 0.0 : size * 0.04; // tiny icons need every pixel
        var avail = size - (2 * margin);
        var scale = Math.Min(avail / image.PixelWidth, avail / image.PixelHeight);
        var w = image.PixelWidth * scale;
        var h = image.PixelHeight * scale;

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(image, new Rect((size - w) / 2, (size - h) / 2, w, h));
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    public static IReadOnlyList<(int Size, BitmapSource Image)> IconFrames(byte[] ico)
    {
        using var ms = new MemoryStream(ico);
        var decoder = new IconBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return [.. decoder.Frames
            .GroupBy(f => f.PixelWidth)
            .Select(g => (g.Key, (BitmapSource)g.First()))
            .OrderBy(f => f.Key)];
    }

    /// <summary>
    /// A plain-language warning when the icon is likely to be unreadable on the taskbar, or null.
    /// Heuristics: a very wide logo becomes tiny in a square; a 16 px image with almost no tonal
    /// variation reads as a solid blob.
    /// </summary>
    public static string? LegibilityWarning(BitmapSource logo, BitmapSource? icon16)
    {
        var aspect = (double)Math.Max(logo.PixelWidth, logo.PixelHeight) / Math.Min(logo.PixelWidth, logo.PixelHeight);
        if (aspect > 1.8)
        {
            return "This logo is wide, so it becomes very small inside a square icon. A square symbol (without the text) works better on the taskbar.";
        }

        if (icon16 is null)
        {
            return null;
        }

        var px = new FormatConvertedBitmap(icon16, PixelFormats.Pbgra32, null, 0);
        var buf = new byte[16 * 16 * 4];
        px.CopyPixels(buf, 16 * 4, 0);
        var lums = new List<double>();
        for (var i = 0; i < buf.Length; i += 4)
        {
            if (buf[i + 3] > 128)
            {
                lums.Add(((0.0722 * buf[i]) + (0.7152 * buf[i + 1]) + (0.2126 * buf[i + 2])) / 255.0);
            }
        }

        if (lums.Count < 24)
        {
            return "At 16 px the icon is mostly transparent and will be hard to see. Use a bolder logo or a filled background.";
        }

        var mean = lums.Average();
        var sd = Math.Sqrt(lums.Average(l => (l - mean) * (l - mean)));
        return sd < 0.08
            ? "At 16 px the icon looks like a solid block: the details disappear. Use a simpler mark with stronger contrast."
            : null;
    }

    public static byte[] EncodePng(BitmapSource source)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }
}
