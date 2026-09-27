using GymPro.Core.Entities;
using GymPro.Core.Rules;
using GymPro.Core.Security;
using Microsoft.EntityFrameworkCore;

namespace GymPro.Data.Services;

public sealed record SubscriptionRow(
    int Id,
    string PlanName,
    PlanKind Kind,
    DateOnly StartDate,
    DateOnly? EndDate,
    decimal Price,
    decimal AmountPaid,
    SubscriptionStatus Status,
    int SessionsUsed,
    SubscriptionState State,
    long Version,
    int PauseDaysAllowed = 0)
{
    public bool IsPaused => State.Block == CheckInBlock.Paused;
    public bool CanPause => PauseDaysAllowed > 0 && !IsPaused && State.PauseDaysLeft > 0
        && State.Block is CheckInBlock.None or CheckInBlock.AlreadyCheckedInToday;

    /// <summary>End date including pause extensions, or "No expiry".</summary>
    public string EndsText => State.EffectiveEnd?.ToString("yyyy-MM-dd") ?? "No expiry";

    public decimal Balance => Price - AmountPaid;
    public StatusTone Tone => State.Tone;
    public string StatusText => State.Block switch
    {
        CheckInBlock.None when State.ExpiringSoon => "Expiring soon",
        CheckInBlock.None => "Active",
        CheckInBlock.AlreadyCheckedInToday => "Checked in today",
        CheckInBlock.NotStarted => "Starts " + StartDate.ToString("yyyy-MM-dd"),
        CheckInBlock.Expired => "Expired",
        CheckInBlock.NoSessionsLeft => "No sessions left",
        CheckInBlock.Cancelled => "Cancelled",
        CheckInBlock.Paused => "Paused",
        _ => State.Block.ToString(),
    };
    public string Remaining => (State.SessionsLeft, State.DaysLeft) switch
    {
        ({ } s, { } d) => $"{s} sessions · {d}d",
        ({ } s, null) => $"{s} sessions",
        (null, { } d) => $"{d} days",
        _ => "Unlimited",
    };
}

/// <summary>Front-desk numbers for today.</summary>
public sealed record DashboardStats(int CheckInsToday, int ActiveMembers, int ExpiringSoon, decimal BalancesDue);

public sealed class SubscriptionService(IDbContextFactory<GymDbContext> factory, ICurrentUser currentUser, TimeProvider time)
{
    private DateOnly Today => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    public async Task<List<SubscriptionRow>> ListForMemberAsync(int memberId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var today = Today;
        var rows = await db.Subscriptions.AsNoTracking()
            .Where(s => s.MemberId == memberId)
            .OrderByDescending(s => s.StartDate).ThenByDescending(s => s.Id)
            .Select(s => new
            {
                Sub = s,
                PlanName = s.Plan!.Name,
                PlanKind = s.Plan.Kind,
                Used = s.Attendances.Count,
                Today = s.Attendances.Any(a => a.Date == today),
            })
            .ToListAsync(ct);

        return rows.Select(r => new SubscriptionRow(
            r.Sub.Id, r.PlanName, r.PlanKind, r.Sub.StartDate, r.Sub.EndDate, r.Sub.Price, r.Sub.AmountPaid, r.Sub.Status,
            r.Used, MembershipRules.Evaluate(r.Sub, r.Used, r.Today, today), r.Sub.Version, r.Sub.PauseDaysAllowed)).ToList();
    }

    public async Task<DashboardStats> GetDashboardAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var today = Today;
        var soon = today.AddDays(SubscriptionState.ExpiringSoonDays);
        // Paused subscriptions don't count as active; no-expiry packs count while they exist
        // (used-up packs are rare and cheap to include).
        var active = db.Subscriptions.AsNoTracking()
            .Where(s => s.Status == SubscriptionStatus.Active && s.PausedFrom == null && s.StartDate <= today
                && (s.EndDate == null || s.EndDate >= today));

        var checkIns = await db.Attendances.CountAsync(a => a.Date == today, ct);
        var members = await active.Select(s => s.MemberId).Distinct().CountAsync(ct);
        var expiring = await active.Where(s => s.EndDate != null && s.EndDate <= soon).Select(s => s.MemberId).Distinct().CountAsync(ct);

        // SQLite stores decimals as TEXT, so sum on the client; the active set is small.
        var money = await db.Subscriptions.AsNoTracking()
            .Where(s => s.Status == SubscriptionStatus.Active)
            .Select(s => new { s.Price, s.AmountPaid })
            .ToListAsync(ct);
        return new DashboardStats(checkIns, members, expiring, money.Sum(m => Math.Max(0, m.Price - m.AmountPaid)));
    }

    public async Task<Subscription> SellAsync(int memberId, int planId, DateOnly start, decimal amountPaid, string? notes, CancellationToken ct = default)
    {
        Permissions.Demand(currentUser, Permission.SellSubscriptions);
        await using var db = await factory.CreateDbContextAsync(ct);
        var plan = await db.Plans.SingleAsync(p => p.Id == planId, ct);
        if (!plan.IsActive)
        {
            throw new InvalidOperationException("This plan is no longer sold.");
        }

        if (amountPaid < 0 || amountPaid > plan.Price)
        {
            throw new ArgumentException($"Amount paid must be between 0 and {plan.Price}.");
        }

        var sub = new Subscription
        {
            MemberId = memberId,
            PlanId = plan.Id,
            StartDate = start,
            EndDate = MembershipRules.EndDateFor(plan, start),
            SessionsAllowed = plan.Kind == PlanKind.SessionPack ? plan.SessionCount : null,
            PauseDaysAllowed = plan.AllowPause ? plan.MaxPauseDays : 0,
            Price = plan.Price,
            AmountPaid = amountPaid,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
        };
        db.Subscriptions.Add(sub);
        await db.SaveChangesAsync(ct);
        return sub;
    }

    public async Task AddPaymentAsync(int subscriptionId, long version, decimal amount, CancellationToken ct = default)
    {
        Permissions.Demand(currentUser, Permission.SellSubscriptions);
        await using var db = await factory.CreateDbContextAsync(ct);
        var sub = await db.Subscriptions.SingleAsync(s => s.Id == subscriptionId, ct);
        db.Entry(sub).Property(s => s.Version).OriginalValue = version;
        if (amount <= 0 || amount > sub.Balance)
        {
            throw new ArgumentException($"Payment must be between 0 and the balance ({sub.Balance}).");
        }

        sub.AmountPaid += amount;
        await db.SaveChangesAsync(ct);
    }

    public async Task CancelAsync(int subscriptionId, long version, CancellationToken ct = default)
    {
        Permissions.Demand(currentUser, Permission.ManagePlans); // managers only: cancelling affects revenue
        await using var db = await factory.CreateDbContextAsync(ct);
        var sub = await db.Subscriptions.SingleAsync(s => s.Id == subscriptionId, ct);
        db.Entry(sub).Property(s => s.Version).OriginalValue = version;
        sub.Status = SubscriptionStatus.Cancelled;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Freezes the subscription from today: no check-ins, and the days don't count down.</summary>
    public async Task PauseAsync(int subscriptionId, long version, CancellationToken ct = default)
    {
        Permissions.Demand(currentUser, Permission.SellSubscriptions);
        var today = Today;
        await using var db = await factory.CreateDbContextAsync(ct);
        var sub = await db.Subscriptions.SingleAsync(s => s.Id == subscriptionId, ct);
        db.Entry(sub).Property(s => s.Version).OriginalValue = version;
        MembershipRules.EndPause(sub, today); // tidy up a pause that already ran out
        if (MembershipRules.CannotPause(sub, today) is { } reason)
        {
            throw new InvalidOperationException(reason);
        }

        sub.PausedFrom = today;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Ends the pause; the end date moves back by the days it lasted. Returns those days.</summary>
    public async Task<int> ResumeAsync(int subscriptionId, long version, CancellationToken ct = default)
    {
        Permissions.Demand(currentUser, Permission.SellSubscriptions);
        await using var db = await factory.CreateDbContextAsync(ct);
        var sub = await db.Subscriptions.SingleAsync(s => s.Id == subscriptionId, ct);
        db.Entry(sub).Property(s => s.Version).OriginalValue = version;
        if (sub.PausedFrom is null)
        {
            throw new InvalidOperationException("The subscription isn't paused.");
        }

        var days = MembershipRules.EndPause(sub, Today);
        await db.SaveChangesAsync(ct);
        return days;
    }

    /// <summary>Records today's visit. Rules are re-checked inside the write so a stale screen can't bypass them.</summary>
    public async Task<SubscriptionState> CheckInAsync(int subscriptionId, CancellationToken ct = default)
    {
        Permissions.Demand(currentUser, Permission.CheckIn);
        var today = Today;
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var sub = await db.Subscriptions.SingleAsync(s => s.Id == subscriptionId, ct);
        if (sub.PausedFrom is not null && !MembershipRules.GetPause(sub, today).IsPaused)
        {
            MembershipRules.EndPause(sub, today); // pause ran out of days: make it permanent before checking in
        }

        var used = await db.Attendances.CountAsync(a => a.SubscriptionId == sub.Id, ct);
        var already = await db.Attendances.AnyAsync(a => a.SubscriptionId == sub.Id && a.Date == today, ct);
        var state = MembershipRules.Evaluate(sub, used, already, today);
        if (!state.CanCheckIn)
        {
            throw new InvalidOperationException(MembershipRules.Describe(state.Block));
        }

        db.Attendances.Add(new Attendance
        {
            MemberId = sub.MemberId,
            SubscriptionId = sub.Id,
            Date = today,
            CheckedInAt = time.GetLocalNow().DateTime,
            RecordedByUserId = currentUser.UserId,
        });

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (MemberService.IsUniqueViolation(ex))
        {
            throw new InvalidOperationException(MembershipRules.Describe(CheckInBlock.AlreadyCheckedInToday), ex);
        }

        return MembershipRules.Evaluate(sub, used + 1, true, today);
    }
}
