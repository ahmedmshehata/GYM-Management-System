using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymPro.App.Infrastructure;
using GymPro.Core.Entities;
using GymPro.Data.Import;
using GymPro.Data.Services;

namespace GymPro.App.ViewModels;

public sealed partial class PlansViewModel(PlanService plans) : ViewModelBase
{
    public ObservableCollection<Plan> Items { get; } = [];
    public PlanKind[] Kinds { get; } = Enum.GetValues<PlanKind>();

    [ObservableProperty]
    public partial Plan? Selected { get; set; }

    /// <summary>Editable copy so an abandoned edit doesn't change the grid row.</summary>
    [ObservableProperty]
    public partial Plan? Edit { get; set; }

    partial void OnSelectedChanged(Plan? value) => Edit = value is null ? null : new Plan
    {
        Id = value.Id,
        Version = value.Version,
        Name = value.Name,
        Kind = value.Kind,
        DurationMonths = value.DurationMonths,
        SessionCount = value.SessionCount,
        SessionsExpire = value.SessionsExpire,
        AllowPause = value.AllowPause,
        MaxPauseDays = value.MaxPauseDays,
        Price = value.Price,
        IsActive = value.IsActive,
    };

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        Items.Clear();
        foreach (var p in await plans.ListAsync(activeOnly: false))
        {
            Items.Add(p);
        }
    });

    [RelayCommand]
    private void New()
    {
        Selected = null;
        Edit = new Plan { Name = "", DurationMonths = 1, IsActive = true };
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Edit is not null && await RunAsync(() => plans.SaveAsync(Edit), "Plan saved."))
        {
            await LoadAsync();
        }
    }

    protected override Task OnConcurrencyConflictAsync() => LoadAsync();
}

public sealed partial class UsersViewModel(UserService users) : ViewModelBase
{
    public ObservableCollection<UserRow> Items { get; } = [];
    public UserRole[] Roles { get; } = Enum.GetValues<UserRole>();

    [ObservableProperty]
    public partial UserRow? Selected { get; set; }

    [ObservableProperty]
    public partial string? EditDisplayName { get; set; }

    [ObservableProperty]
    public partial UserRole EditRole { get; set; }

    [ObservableProperty]
    public partial bool EditActive { get; set; }

    [ObservableProperty]
    public partial string? NewUserName { get; set; }

    [ObservableProperty]
    public partial string? NewDisplayName { get; set; }

    [ObservableProperty]
    public partial UserRole NewRole { get; set; }

    partial void OnSelectedChanged(UserRow? value)
    {
        EditDisplayName = value?.DisplayName;
        EditRole = value?.Role ?? UserRole.Reception;
        EditActive = value?.IsActive ?? false;
    }

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        Items.Clear();
        foreach (var u in await users.ListAsync())
        {
            Items.Add(u);
        }
    });

    /// <summary>The PasswordBox is passed in because its value is deliberately not bindable.</summary>
    [RelayCommand]
    private async Task CreateAsync(PasswordBox? password)
    {
        if (await RunAsync(() => users.CreateAsync(NewUserName ?? "", NewDisplayName ?? "", NewRole, password?.Password ?? ""),
                "User created. They must change the password at first sign-in."))
        {
            NewUserName = NewDisplayName = null;
            password?.Clear();
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task UpdateAsync()
    {
        if (Selected is { } s &&
            await RunAsync(() => users.UpdateAsync(s with { DisplayName = EditDisplayName ?? s.DisplayName, Role = EditRole, IsActive = EditActive }), "User updated."))
        {
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task ResetPasswordAsync(PasswordBox? password)
    {
        if (Selected is { } s && await RunAsync(() => users.ResetPasswordAsync(s.Id, password?.Password ?? ""), $"Password reset for {s.UserName}."))
        {
            password?.Clear();
        }
    }

    protected override Task OnConcurrencyConflictAsync() => LoadAsync();
}

public sealed partial class AuditViewModel(AuditQueryService audit) : ViewModelBase
{
    public ObservableCollection<AuditLog> Items { get; } = [];
    public string[] EntityTypes { get; } = ["", nameof(Member), nameof(MemberPhoto), nameof(Subscription), nameof(Attendance), nameof(Plan), nameof(AppUser)];
    public object[] Actions { get; } = [.. new object[] { "" }.Concat(Enum.GetValues<AuditAction>().Cast<object>())];

    [ObservableProperty]
    public partial DateTime From { get; set; } = DateTime.Today.AddDays(-7);

    [ObservableProperty]
    public partial DateTime To { get; set; } = DateTime.Today;

    [ObservableProperty]
    public partial string? UserName { get; set; }

    [ObservableProperty]
    public partial string? EntityType { get; set; }

    [ObservableProperty]
    public partial object? Action { get; set; }

    [ObservableProperty]
    public partial string? Text { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDetails))]
    public partial AuditLog? Selected { get; set; }

    public string? SelectedDetails => Pretty(Selected?.Details);

    [RelayCommand]
    private Task SearchAsync() => RunAsync(async () =>
    {
        Items.Clear();
        var filter = new AuditFilter(DateOnly.FromDateTime(From), DateOnly.FromDateTime(To), UserName, EntityType, Action as AuditAction?, Text);
        foreach (var a in await audit.QueryAsync(filter))
        {
            Items.Add(a);
        }

        Status = $"{Items.Count} entries" + (Items.Count == 1000 ? " (first 1000 shown; narrow the filter)" : "");
    });

    private static string? Pretty(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, PrettyJson);
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

public sealed partial class ImportViewModel(LegacyImporter importer, PhotoMaintenanceService photos) : ViewModelBase
{
    [ObservableProperty]
    public partial string? PhotoStatsText { get; set; }

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        var st = await photos.GetStatsAsync();
        PhotoStatsText = $"{st.Photos:N0} photos using {Mb(st.PhotoBytes)} · database file {Mb(st.DatabaseFileBytes)}"
            + (st.MissingThumbnails > 0 ? $" · {st.MissingThumbnails:N0} thumbnails still being prepared" : "");
    });

    /// <summary>Shrinks every photo to the stored size (600 px) after a full backup, then compacts the database.</summary>
    [RelayCommand]
    private async Task OptimizePhotosAsync()
    {
        if (!Dialogs.Confirm(
                "Shrink all member photos to 600 px and compact the database?\n\n" +
                "A full backup is made first, next to the database. Other PCs should close GymPro while this runs. " +
                "It can take several minutes for thousands of photos."))
        {
            return;
        }

        Log = "";
        var progress = new Progress<string>(line => Log += line + Environment.NewLine);
        OptimizeReport? report = null;
        await RunAsync(async () => report = await Task.Run(() => photos.OptimizePhotosAsync(progress)));
        if (report is { } r)
        {
            Log += $"{Environment.NewLine}Resized {r.Resized:N0} photos, created {r.ThumbnailsCreated:N0} thumbnails, {r.Unreadable:N0} unreadable (left as they were).{Environment.NewLine}" +
                   $"Photos: {Mb(r.BytesBefore)} -> {Mb(r.BytesAfter)} · database file: {Mb(r.FileBefore)} -> {Mb(r.FileAfter)}{Environment.NewLine}" +
                   $"Backup: {r.BackupPath}{Environment.NewLine}";
            Toast.Show($"Photos optimized: database {Mb(r.FileBefore)} -> {Mb(r.FileAfter)}.");
            await LoadAsync();
        }
    }

    private static string Mb(long bytes) => $"{bytes / 1024d / 1024d:N0} MB";

    [ObservableProperty]
    public partial string? FilePath { get; set; }

    [ObservableProperty]
    public partial string Log { get; set; } = "";

    [RelayCommand]
    private void Browse() => FilePath = Dialogs.OpenFile("Legacy GYM database (*.db)|*.db|All files|*.*") ?? FilePath;

    [RelayCommand]
    private Task ImportAsync() => RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            throw new ArgumentException("Choose the legacy db_at.db file first.");
        }

        Log = "";
        var progress = new Progress<string>(line => Log += line + Environment.NewLine);
        var report = await Task.Run(() => importer.ImportAsync(FilePath, progress));
        Log += Environment.NewLine + string.Join(Environment.NewLine, report.Warnings.Select(w => "WARN  " + w));
        Dialogs.Info("Import finished.\n\n" + report);
    });
}
