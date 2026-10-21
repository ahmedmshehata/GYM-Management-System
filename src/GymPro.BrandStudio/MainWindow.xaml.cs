using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace GymPro.BrandStudio;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // BrandStudio.exe <brand pack folder | brand.json | .brandpack>
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && DataContext is StudioViewModel vm)
        {
            Loaded += (_, _) => vm.OpenPath(args[1]);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (DataContext is StudioViewModel vm && !vm.ConfirmClose())
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }

    // Keep the build log scrolled to the newest line.
    private void OnLogChanged(object sender, TextChangedEventArgs e) => ((TextBox)sender).ScrollToEnd();
}
