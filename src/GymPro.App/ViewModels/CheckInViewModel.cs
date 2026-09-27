using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymPro.App.Infrastructure;
using GymPro.Core.Entities;
using GymPro.Data.Services;

namespace GymPro.App.ViewModels;

/// <summary>Front-desk screen: find a member by number/name/phone and record the visit.</summary>
public sealed partial class CheckInViewModel(MemberService members, SubscriptionService subscriptions) : ViewModelBase
{
    public ObservableCollection<MemberRow> Results { get; } = [];
    public ObservableCollection<SubscriptionRow> Subscriptions { get; } = [];

    [ObservableProperty]
    public partial DashboardStats? Stats { get; set; }

    [ObservableProperty]
    public partial string? Search { get; set; }

    [ObservableProperty]
    public partial MemberRow? SelectedMember { get; set; }

    [ObservableProperty]
    public partial Member? Detail { get; set; }

    /// <summary>What the member still owes across active subscriptions; shown as one badge instead of a column.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBalanceDue))]
    public partial decimal BalanceDue { get; set; }

    public bool HasBalanceDue => BalanceDue > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckInCommand))]
    public partial SubscriptionRow? SelectedSubscription { get; set; }

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            Stats = await subscriptions.GetDashboardAsync();
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            Stats = null; // dashboard is a nicety; never block check-in on it
        }
    }

    [RelayCommand]
    private Task FindAsync() => RunAsync(async () =>
    {
        Results.Clear();
        foreach (var m in await members.SearchAsync(Search, 50))
        {
            Results.Add(m);
        }

        // Typing an exact card number and pressing Enter selects that member straight away.
        SelectedMember = Results.Count == 1 ? Results[0] : null;
        Status = Results.Count == 0 ? $"No member matches \"{Search}\"." : null;
    });

    partial void OnSelectedMemberChanged(MemberRow? value) => _ = LoadMemberAsync(value);

    private async Task LoadMemberAsync(MemberRow? row)
    {
        Subscriptions.Clear();
        Detail = null;
        BalanceDue = 0;
        if (row is null)
        {
            return;
        }

        Detail = await members.GetAsync(row.Id);
        foreach (var s in await subscriptions.ListForMemberAsync(row.Id))
        {
            Subscriptions.Add(s);
        }

        BalanceDue = Subscriptions.Where(s => s.Status == SubscriptionStatus.Active).Sum(s => Math.Max(0, s.Balance));
        SelectedSubscription = Subscriptions.FirstOrDefault(s => s.State.CanCheckIn) ?? Subscriptions.FirstOrDefault();
    }

    private bool CanCheckIn() => SelectedSubscription?.State.CanCheckIn == true;

    [RelayCommand(CanExecute = nameof(CanCheckIn))]
    private async Task CheckInAsync()
    {
        var sub = SelectedSubscription!;
        string? message = null;
        var ok = await RunAsync(async () =>
        {
            var state = await subscriptions.CheckInAsync(sub.Id);
            message = $"{SelectedMember?.FullName} checked in. " +
                (state.SessionsLeft is { } s ? $"{s} sessions left, " : "") + $"{state.DaysLeft} days left.";
        });

        await LoadMemberAsync(SelectedMember);
        await LoadAsync();
        if (ok)
        {
            Status = message;
            Toast.Show(message!);
        }
    }
}
