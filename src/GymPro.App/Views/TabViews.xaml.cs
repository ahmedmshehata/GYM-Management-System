using System.Windows.Controls;
using System.Windows.Input;
using GymPro.App.ViewModels;

namespace GymPro.App.Views;

// Each page refreshes its data when it's shown, so users see changes made on other PCs.

public partial class CheckInView : UserControl
{
    public CheckInView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            MembersView.Run((DataContext as CheckInViewModel)?.LoadCommand);
            SearchBox.Focus();
            SearchBox.SelectAll();
        };
    }
}

public partial class MembersView : UserControl
{
    public MembersView()
    {
        InitializeComponent();
        Loaded += (_, _) => Run((DataContext as MembersViewModel)?.LoadCommand);
    }

    internal static void Run(ICommand? command) => command?.Execute(null);

    /// <summary>Infinite scroll: fetch the next page when the list is scrolled near the bottom.</summary>
    private void OnListScroll(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeight > 0 && e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 4 && DataContext is MembersViewModel vm && vm.HasMore)
        {
            vm.LoadMoreCommand.Execute(null);
        }
    }
}

public partial class PlansView : UserControl
{
    public PlansView()
    {
        InitializeComponent();
        Loaded += (_, _) => MembersView.Run((DataContext as PlansViewModel)?.LoadCommand);
    }
}

public partial class UsersView : UserControl
{
    public UsersView()
    {
        InitializeComponent();
        Loaded += (_, _) => MembersView.Run((DataContext as UsersViewModel)?.LoadCommand);
    }
}

public partial class AuditView : UserControl
{
    public AuditView()
    {
        InitializeComponent();
        Loaded += (_, _) => MembersView.Run((DataContext as AuditViewModel)?.SearchCommand);
    }
}

public partial class ImportView : UserControl
{
    public ImportView()
    {
        InitializeComponent();
        Loaded += (_, _) => MembersView.Run((DataContext as ImportViewModel)?.LoadCommand);
    }
}
