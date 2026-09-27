using System.Globalization;

namespace GymPro.Core.Theming;

/// <summary>Small colour helpers (hex parsing, mixing, WCAG 2.x contrast) with no UI dependency, so themes are unit-testable.</summary>
public readonly record struct Rgba(byte A, byte R, byte G, byte B)
{
    public static Rgba Parse(string hex)
    {
        var h = hex.Trim().TrimStart('#');
        uint v = uint.Parse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return h.Length switch
        {
            6 => new Rgba(255, (byte)(v >> 16), (byte)(v >> 8), (byte)v),
            8 => new Rgba((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v),
            _ => throw new FormatException($"'{hex}' is not #RRGGBB or #AARRGGBB."),
        };
    }

    public static bool TryParse(string? hex, out Rgba color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        try
        {
            color = Parse(hex);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            return false;
        }
    }

    public override string ToString() =>
        A == 255 ? $"#{R:X2}{G:X2}{B:X2}" : $"#{A:X2}{R:X2}{G:X2}{B:X2}";
}

public static class ColorMath
{
    public static readonly Rgba White = new(255, 255, 255, 255);
    public static readonly Rgba Black = new(255, 0, 0, 0);

    public static Rgba Mix(Rgba a, Rgba b, double t) => new(
        a.A,
        (byte)Math.Round(a.R + ((b.R - a.R) * t)),
        (byte)Math.Round(a.G + ((b.G - a.G) * t)),
        (byte)Math.Round(a.B + ((b.B - a.B) * t)));

    public static Rgba WithAlpha(Rgba c, byte alpha) => c with { A = alpha };

    /// <summary>Paint a (possibly translucent) colour over an opaque background.</summary>
    public static Rgba Over(Rgba top, Rgba bottom)
    {
        var a = top.A / 255.0;
        return new Rgba(255,
            (byte)Math.Round((top.R * a) + (bottom.R * (1 - a))),
            (byte)Math.Round((top.G * a) + (bottom.G * (1 - a))),
            (byte)Math.Round((top.B * a) + (bottom.B * (1 - a))));
    }

    public static double Luminance(Rgba c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
    }

    /// <summary>WCAG contrast ratio (1..21). Translucent colours are composited over <paramref name="background"/> first.</summary>
    public static double Contrast(Rgba foreground, Rgba background)
    {
        var fg = foreground.A == 255 ? foreground : Over(foreground, background);
        var l1 = Luminance(fg);
        var l2 = Luminance(background);
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    /// <summary>Black or white, whichever reads better on <paramref name="background"/>.</summary>
    public static Rgba BestTextOn(Rgba background) =>
        Contrast(White, background) >= Contrast(Black, background) ? White : Black;

    /// <summary>Darken (on light surfaces) or lighten (on dark ones) until <paramref name="c"/> reaches <paramref name="min"/>:1 against <paramref name="surface"/>.</summary>
    public static Rgba EnsureContrast(Rgba c, Rgba surface, double min)
    {
        var target = Luminance(surface) > 0.5 ? Black : White;
        var result = c;
        for (var step = 1; step <= 20 && Contrast(result, surface) < min; step++)
        {
            result = Mix(c, target, step * 0.05);
        }

        return result;
    }
}
