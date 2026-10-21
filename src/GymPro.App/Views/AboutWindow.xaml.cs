using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;
using GymPro.App.Infrastructure;
using GymPro.Core.Branding;

namespace GymPro.App.Views;

/// <summary>
/// Product, version, the brand's company and support details, and the licence notices.
/// The licence section is always shown, even with "Powered by" hidden: the MIT licence requires
/// keeping the original copyright notice in copies of the software.
/// </summary>
public partial class AboutWindow : Window
{
    public AboutWindow(Branding brand)
    {
        InitializeComponent();
        Title = "About " + brand.ProductName;
        ProductText.Text = brand.ProductName;
        LogoImage.Source = brand.Logo ?? Branding.AppLogo;
        var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        VersionText.Text = "Version " + version.Split('+')[0]; // drop the source-control hash the SDK appends

        Show(DescriptionText, brand.Description);
        Show(CompanyText, brand.CompanyName);
        Show(CopyrightText, brand.Copyright);

        Link(WebsiteLine, WebsiteLink, WebsiteRun, brand.WebsiteUrl, brand.WebsiteUrl);
        Link(EmailLine, EmailLink, EmailRun, brand.SupportEmail is null ? null : "mailto:" + brand.SupportEmail, brand.SupportEmail);
        Show(PhoneText, brand.SupportPhone);
        SupportPanel.Visibility = brand.SupportLine is null ? Visibility.Collapsed : Visibility.Visible;

        Show(PoweredByText, brand.ShowPoweredBy && !string.Equals(brand.ProductName, BrandDefaults.UpstreamProduct, StringComparison.Ordinal)
            ? $"Powered by {BrandDefaults.UpstreamProduct}"
            : null);

        LicenceText.Text = LicenceNotices.Text;
    }

    private static void Show(System.Windows.Controls.TextBlock block, string? text)
    {
        block.Text = text ?? "";
        block.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void Link(System.Windows.Controls.TextBlock line, System.Windows.Documents.Hyperlink link, System.Windows.Documents.Run run, string? uri, string? text)
    {
        if (uri is null || !Uri.TryCreate(uri, UriKind.Absolute, out var u))
        {
            line.Visibility = Visibility.Collapsed;
            return;
        }

        link.NavigateUri = u;
        run.Text = text;
    }

    private void OnNavigate(object sender, RequestNavigateEventArgs e)
    {
        // Only http(s) and mailto: a brand file must not be able to launch arbitrary programs.
        if (e.Uri.Scheme is "http" or "https" or "mailto")
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }

        e.Handled = true;
    }
}

/// <summary>Licence notices for the software and the components shipped inside it.</summary>
public static class LicenceNotices
{
    private static readonly string Nl = Environment.NewLine;

    // One line per paragraph so the text box wraps it to its width.
    public static string Text { get; } = string.Join(Nl + Nl,
        BrandDefaults.UpstreamProduct + Nl + BrandDefaults.UpstreamUrl,
        "MIT License",
        BrandDefaults.UpstreamCopyright,
        "Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files " +
        "(the \"Software\"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, " +
        "merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is " +
        "furnished to do so, subject to the following conditions:",
        "The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.",
        "THE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF " +
        "MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE " +
        "FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN " +
        "CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.",
        "Third-party components" + Nl +
        "- .NET runtime, WPF, Entity Framework Core, Microsoft.Data.Sqlite, Microsoft.Extensions.*: MIT License, (c) .NET Foundation and Contributors" + Nl +
        "- CommunityToolkit.Mvvm: MIT License, (c) .NET Foundation and Contributors" + Nl +
        "- SQLitePCLRaw: Apache License 2.0, (c) Eric Sink" + Nl +
        "- SQLite: public domain");
}
