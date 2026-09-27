using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymPro.App.Infrastructure;
using GymPro.Core.Entities;
using GymPro.Data.Services;

namespace GymPro.App.ViewModels;

public sealed partial class MembersViewModel(MemberService members, SubscriptionService subscriptions, PlanService plans) : ViewModelBase
{
    public ObservableCollection<MemberRow> Results { get; } = [];
    public ObservableCollection<SubscriptionRow> Subscriptions { get; } = [];
    public ObservableCollection<Plan> Plans { get; } = [];
    public Gender[] Genders { get; } = Enum.GetValues<Gender>();

    private const int PageSize = 100;
    private int _total;
    private bool _loadingPage;

    [ObservableProperty]
    public partial string? Search { get; set; }

    /// <summary>"Showing 100 of 4,582": makes it obvious more rows load as you scroll.</summary>
    [ObservableProperty]
    public partial string? CountText { get; set; }

    /// <summary>Photo cards instead of the compact list. Remembered on this PC.</summary>
    [ObservableProperty]
    public partial bool ShowCards { get; set; } = GymPro.App.Theming.LocalUiSettings.Load().MemberCards;

    partial void OnShowCardsChanged(bool value) =>
        GymPro.App.Theming.LocalUiSettings.Save(GymPro.App.Theming.LocalUiSettings.Load() with { MemberCards = value });

    public bool HasMore => Results.Count < _total;

    [RelayCommand]
    private void ViewList() => ShowCards = false;

    [RelayCommand]
    private void ViewCards() => ShowCards = true;

    [ObservableProperty]
    public partial MemberRow? SelectedRow { get; set; }

    /// <summary>The member being edited (a detached copy; Id 0 = new).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExisting))]
    public partial Member? Edit { get; set; }

    [ObservableProperty]
    public partial byte[]? PhotoBytes { get; set; }

    // null = unchanged, empty = remove, bytes = replace
    private byte[]? _pendingPhoto;

    public bool IsExisting => Edit is { Id: > 0 };

    // ---- sell subscription form
    [ObservableProperty]
    public partial Plan? SellPlan { get; set; }

    [ObservableProperty]
    public partial DateTime SellStart { get; set; } = DateTime.Today;

    [ObservableProperty]
    public partial string? SellPaid { get; set; }

    [ObservableProperty]
    public partial SubscriptionRow? SelectedSubscription { get; set; }

    [ObservableProperty]
    public partial string? PaymentAmount { get; set; }

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        Plans.Clear();
        foreach (var p in await plans.ListAsync(activeOnly: true))
        {
            Plans.Add(p);
        }

        await FindAsync();
    });

    /// <summary>New search: first page only. More pages load as the list is scrolled (<see cref="LoadMoreCommand"/>).</summary>
    [RelayCommand]
    private async Task FindAsync()
    {
        Results.Clear();
        _total = 0;
        await LoadMoreAsync();
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (_loadingPage || (Results.Count > 0 && !HasMore))
        {
            return;
        }

        _loadingPage = true;
        try
        {
            var page = await members.SearchPageAsync(Search, Results.Count, PageSize);
            _total = page.Total;
            foreach (var m in page.Rows)
            {
                Results.Add(m);
            }

            CountText = _total == 0 ? "No members found"
                : Results.Count < _total ? $"Showing {Results.Count:N0} of {_total:N0} · scroll for more"
                : $"{_total:N0} member{(_total == 1 ? "" : "s")}";
            OnPropertyChanged(nameof(HasMore));
        }
        finally
        {
            _loadingPage = false;
        }
    }

    /// <summary>Refreshes just one row after a save, so the list keeps its scroll position.</summary>
    private async Task RefreshRowAsync(Member saved)
    {
        var rows = (await members.SearchPageAsync(saved.MemberNo.ToString(CultureInfo.InvariantCulture), 0, 1)).Rows;
        var fresh = rows.Count > 0 ? rows[0] : null;
        if (fresh is null || fresh.Id != saved.Id)
        {
            return;
        }

        var index = Results.ToList().FindIndex(r => r.Id == saved.Id);
        if (index >= 0)
        {
            Results[index] = fresh;
        }
        else
        {
            Results.Insert(0, fresh);
            _total++;
        }
    }

    partial void OnSelectedRowChanged(MemberRow? value)
    {
        if (value is not null)
        {
            _ = OpenAsync(value.Id);
        }
    }

    private async Task OpenAsync(int id)
    {
        Edit = await members.GetAsync(id);
        PhotoBytes = Edit?.Photo?.Data;
        _pendingPhoto = null;
        await LoadSubscriptionsAsync();
    }

    private async Task LoadSubscriptionsAsync()
    {
        Subscriptions.Clear();
        if (Edit is { Id: > 0 } m)
        {
            foreach (var s in await subscriptions.ListForMemberAsync(m.Id))
            {
                Subscriptions.Add(s);
            }
        }
    }

    [RelayCommand]
    private async Task NewAsync()
    {
        SelectedRow = null;
        Edit = new Member { FullName = "", MemberNo = await members.NextMemberNoAsync() };
        PhotoBytes = null;
        _pendingPhoto = null;
        Subscriptions.Clear();
    }

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        if (Edit is null)
        {
            return;
        }

        var saved = await members.SaveAsync(Edit, _pendingPhoto);
        await RefreshRowAsync(saved);
        await OpenAsync(saved.Id);
    }, "Member saved.");

    protected override async Task OnConcurrencyConflictAsync()
    {
        if (Edit is { Id: > 0 } m)
        {
            await OpenAsync(m.Id);
        }
    }

    [RelayCommand]
    private void BrowsePhoto()
    {
        var file = Dialogs.OpenFile("Images|*.jpg;*.jpeg;*.png;*.bmp");
        if (file is null)
        {
            return;
        }

        try
        {
            _pendingPhoto = Images.NormalizePhoto(file);
            PhotoBytes = _pendingPhoto;
        }
        catch (Exception ex) when (ex is NotSupportedException or System.IO.IOException or System.IO.FileFormatException or ArgumentException)
        {
            Dialogs.Error("That file is not a supported image.");
        }
    }

    /// <summary>Webcam capture; with several cameras the dialog lets the user choose.</summary>
    [RelayCommand]
    private void TakePhoto()
    {
        var dlg = new GymPro.App.Views.CameraWindow { Owner = System.Windows.Application.Current.MainWindow };
        if (dlg.ShowDialog() == true && dlg.Photo is { } photo)
        {
            _pendingPhoto = photo;
            PhotoBytes = photo;
            Toast.Show("Photo captured. Press Save to keep it.", GymPro.Core.Rules.StatusTone.Info);
        }
    }

    [RelayCommand]
    private void RemovePhoto()
    {
        _pendingPhoto = [];
        PhotoBytes = null;
    }

    [RelayCommand]
    private Task SellAsync() => RunAsync(async () =>
    {
        if (Edit is not { Id: > 0 } m || SellPlan is null)
        {
            throw new InvalidOperationException("Save the member and choose a plan first.");
        }

        var paid = ParseMoney(SellPaid, SellPlan.Price);
        await subscriptions.SellAsync(m.Id, SellPlan.Id, DateOnly.FromDateTime(SellStart), paid, null);
        SellPaid = null;
        await LoadSubscriptionsAsync();
    }, "Subscription added.");

    [RelayCommand]
    private Task AddPaymentAsync() => RunAsync(async () =>
    {
        var s = SelectedSubscription ?? throw new InvalidOperationException("Select a subscription.");
        await subscriptions.AddPaymentAsync(s.Id, s.Version, ParseMoney(PaymentAmount, s.Balance));
        PaymentAmount = null;
        await LoadSubscriptionsAsync();
    }, "Payment recorded.");

    [RelayCommand]
    private Task CancelSubscriptionAsync() => RunAsync(async () =>
    {
        var s = SelectedSubscription ?? throw new InvalidOperationException("Select a subscription.");
        if (Dialogs.Confirm($"Cancel '{s.PlanName}' ({s.StartDate:yyyy-MM-dd} to {s.EndsText})?"))
        {
            await subscriptions.CancelAsync(s.Id, s.Version);
            await LoadSubscriptionsAsync();
        }
    });

    [RelayCommand]
    private Task PauseAsync() => RunAsync(async () =>
    {
        var s = SelectedSubscription ?? throw new InvalidOperationException("Select a subscription.");
        await subscriptions.PauseAsync(s.Id, s.Version);
        await LoadSubscriptionsAsync();
    }, "Subscription paused. Its days won't count down until it's resumed.");

    [RelayCommand]
    private async Task ResumeAsync()
    {
        var s = SelectedSubscription;
        var days = 0;
        if (s is not null && await RunAsync(async () => days = await subscriptions.ResumeAsync(s.Id, s.Version)))
        {
            Toast.Show(days == 0 ? "Subscription resumed." : $"Subscription resumed. End date moved {days} day{(days == 1 ? "" : "s")} later.");
            await LoadSubscriptionsAsync();
        }
    }

    private static decimal ParseMoney(string? text, decimal fallback) =>
        string.IsNullOrWhiteSpace(text) ? fallback
        : decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var v) ? v
        : throw new ArgumentException($"'{text}' is not a valid amount.");
}
