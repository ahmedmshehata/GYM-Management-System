using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using GymPro.Core.Theming;

namespace GymPro.BrandStudio;

/// <summary>Field → message, bindable as <c>{Binding Errors[ProductName]}</c> for inline errors next to each field.</summary>
public sealed class ErrorBag : INotifyPropertyChanged
{
    private Dictionary<string, string> _errors = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public string? this[string field] => _errors.GetValueOrDefault(field);

    public bool Any => _errors.Count > 0;

    public string Summary => _errors.Count == 0
        ? "Ready."
        : $"{_errors.Count} thing{(_errors.Count == 1 ? "" : "s")} to fix: " + string.Join(" ", _errors.Values);

    public void Set(IEnumerable<(string Field, string Message)> errors)
    {
        _errors = errors.GroupBy(e => e.Field).ToDictionary(g => g.Key, g => g.First().Message);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Any)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary)));
    }
}

/// <summary>A theme with a tick box for "offer this theme".</summary>
public sealed class ThemeChoice(ThemeDefinition theme, Action changed) : INotifyPropertyChanged
{
    private bool _isAllowed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ThemeDefinition Theme { get; } = theme;
    public string Id => Theme.Id;
    public string Label => $"{Theme.DisplayName}  ({Theme.Family})";

    public bool IsAllowed
    {
        get => _isAllowed;
        set
        {
            if (_isAllowed != value)
            {
                _isAllowed = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAllowed)));
                changed();
            }
        }
    }
}

/// <summary>Brushes for the live preview, resolved by the app's own ThemeResolver (same colours the app will show).</summary>
public sealed class PreviewPalette
{
    public PreviewPalette(ThemeDefinition theme, string? brandAccent)
    {
        var t = ThemeResolver.Resolve(theme, brandAccent);
        Brush B(string k) => Brush(t[k]);
        Window = B("Window");
        Surface = B("Surface");
        Text = B("Text");
        TextMuted = B("TextMuted");
        Border = B("Border");
        Header = B("Header");
        OnHeader = B("OnHeader");
        HeaderMuted = B("HeaderMuted");
        Nav = B("Nav");
        OnNav = B("OnNav");
        NavSelected = B("NavSelected");
        OnNavSelected = B("OnNavSelected");
        NavIndicator = B("NavIndicator");
        Accent = B("Accent");
        OnAccent = B("OnAccent");
        AccentStrong = B("AccentStrong");
        Input = B("Input");
        InputBorder = B("InputBorder");
        Tokens = t;
    }

    public IReadOnlyDictionary<string, Rgba> Tokens { get; }
    public Brush Window { get; }
    public Brush Surface { get; }
    public Brush Text { get; }
    public Brush TextMuted { get; }
    public Brush Border { get; }
    public Brush Header { get; }
    public Brush OnHeader { get; }
    public Brush HeaderMuted { get; }
    public Brush Nav { get; }
    public Brush OnNav { get; }
    public Brush NavSelected { get; }
    public Brush OnNavSelected { get; }
    public Brush NavIndicator { get; }
    public Brush Accent { get; }
    public Brush OnAccent { get; }
    public Brush AccentStrong { get; }
    public Brush Input { get; }
    public Brush InputBorder { get; }

    public static SolidColorBrush Brush(Rgba c)
    {
        var b = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }
}

/// <summary>One line of the contrast report.</summary>
public sealed record ContrastCheck(string What, double Ratio, double Required)
{
    public bool Passes => Ratio >= Required;
    public string Result => $"{Ratio:0.0}:1 (needs {Required:0.#}:1)";
    public string Glyph => Passes ? "" : "";
}

public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null or "" ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class BoolToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Pass = Freeze(Color.FromRgb(0x15, 0x80, 0x3D));
    private static readonly SolidColorBrush Fail = Freeze(Color.FromRgb(0xB4, 0x23, 0x18));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? Pass : Fail;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();

    private static SolidColorBrush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
