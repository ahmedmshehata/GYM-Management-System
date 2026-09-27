using GymPro.Core.Entities;

namespace GymPro.Core.Rules;

public enum CheckInBlock
{
    None,
    Cancelled,
    NotStarted,
    Expired,
    NoSessionsLeft,
    AlreadyCheckedInToday,
    Paused,
}

/// <summary>Visual severity for status badges: lets reception spot "expiring soon" at a glance.</summary>
public enum StatusTone
{
    Neutral,
    Success,
    Info,
    Warning,
    Danger,
}

/// <param name="DaysLeft">Days of validity left including today; null = no expiry date.</param>
/// <param name="SessionsLeft">Sessions left; null = unlimited visits.</param>
/// <param name="EffectiveEnd">End date including any pause extension; null = no expiry.</param>
public sealed record SubscriptionState(
    int? DaysLeft,
    int? SessionsLeft,
    CheckInBlock Block,
    DateOnly? EffectiveEnd = null,
    int PauseDaysLeft = 0)
{
    public const int ExpiringSoonDays = 7;
    public const int FewSessionsLeft = 2;

    public bool CanCheckIn => Block == CheckInBlock.None;

    public bool ExpiringSoon => CanCheckIn && (DaysLeft <= ExpiringSoonDays || SessionsLeft <= FewSessionsLeft);

    public StatusTone Tone => Block switch
    {
        CheckInBlock.None => ExpiringSoon ? StatusTone.Warning : StatusTone.Success,
        CheckInBlock.AlreadyCheckedInToday => StatusTone.Info,
        CheckInBlock.NotStarted or CheckInBlock.Paused => StatusTone.Neutral,
        _ => StatusTone.Danger,
    };
}

/// <summary>Where a pause stands on a given day.</summary>
/// <param name="IsPaused">Still paused (inside the allowance).</param>
/// <param name="DaysUsed">Days of this pause that count, capped at the remaining allowance.</param>
/// <param name="Overran">The pause ran past its allowance and has ended on its own.</param>
public readonly record struct PauseStatus(bool IsPaused, int DaysUsed, bool Overran);

/// <summary>Pure business rules, ported from the legacy frmAttend.GetRemind logic (minus its bugs).</summary>
public static class MembershipRules
{
    /// <summary>
    /// End date for a new subscription: start + months, inclusive. Session packs that don't expire
    /// have no end date at all.
    /// </summary>
    public static DateOnly? EndDateFor(Plan plan, DateOnly start) =>
        plan.Kind == PlanKind.SessionPack && !plan.SessionsExpire
            ? null
            : start.AddMonths(Math.Max(1, plan.DurationMonths)).AddDays(-1);

    public static int PauseAllowanceLeft(Subscription s) => Math.Max(0, s.PauseDaysAllowed - s.PausedDays);

    /// <summary>
    /// A pause is counted in whole days from <see cref="Subscription.PausedFrom"/> up to (not including) today.
    /// Once it uses up the allowance it ends by itself, so a forgotten "resume" can't freeze a membership forever.
    /// </summary>
    public static PauseStatus GetPause(Subscription s, DateOnly today)
    {
        if (s.PausedFrom is not { } from)
        {
            return default;
        }

        var allowance = PauseAllowanceLeft(s);
        var elapsed = Math.Max(0, today.DayNumber - from.DayNumber);
        return elapsed >= allowance
            ? new PauseStatus(false, allowance, true)
            : new PauseStatus(true, elapsed, false);
    }

    public static SubscriptionState Evaluate(Subscription s, int sessionsUsed, bool checkedInToday, DateOnly today)
    {
        var pause = GetPause(s, today);

        // While paused the end date keeps moving with the calendar, so "days left" stays frozen.
        DateOnly? end = s.EndDate?.AddDays(pause.DaysUsed);
        int? daysLeft = end is { } e
            ? today > e ? 0 : e.DayNumber - Math.Max(today.DayNumber, s.StartDate.DayNumber) + 1
            : null;
        int? sessionsLeft = s.SessionsAllowed is { } allowed ? Math.Max(0, allowed - sessionsUsed) : null;

        var block = s.Status == SubscriptionStatus.Cancelled ? CheckInBlock.Cancelled
            : pause.IsPaused ? CheckInBlock.Paused
            : today < s.StartDate ? CheckInBlock.NotStarted
            : today > end ? CheckInBlock.Expired
            : sessionsLeft == 0 ? CheckInBlock.NoSessionsLeft
            : checkedInToday ? CheckInBlock.AlreadyCheckedInToday
            : CheckInBlock.None;

        return new SubscriptionState(daysLeft, sessionsLeft, block, end, PauseAllowanceLeft(s) - pause.DaysUsed);
    }

    /// <summary>Why a subscription can't be paused today, or null if it can.</summary>
    public static string? CannotPause(Subscription s, DateOnly today)
    {
        if (s.PauseDaysAllowed <= 0)
        {
            return "This plan doesn't allow pausing.";
        }

        if (s.Status == SubscriptionStatus.Cancelled)
        {
            return "The subscription is cancelled.";
        }

        if (GetPause(s, today).IsPaused)
        {
            return "The subscription is already paused.";
        }

        if (today < s.StartDate)
        {
            return "The subscription hasn't started yet.";
        }

        if (s.EndDate is { } end && today > end.AddDays(GetPause(s, today).DaysUsed))
        {
            return "The subscription has expired.";
        }

        return PauseAllowanceLeft(s) - GetPause(s, today).DaysUsed <= 0 ? "No pause days left on this subscription." : null;
    }

    /// <summary>
    /// Ends a pause (explicit resume, or one that ran out) and pushes the end date back by the days it lasted.
    /// Returns the days added.
    /// </summary>
    public static int EndPause(Subscription s, DateOnly today)
    {
        var pause = GetPause(s, today);
        if (s.PausedFrom is null)
        {
            return 0;
        }

        s.PausedDays += pause.DaysUsed;
        if (s.EndDate is { } end)
        {
            s.EndDate = end.AddDays(pause.DaysUsed);
        }

        s.PausedFrom = null;
        return pause.DaysUsed;
    }

    public static string Describe(CheckInBlock block) => block switch
    {
        CheckInBlock.None => "OK",
        CheckInBlock.Cancelled => "Subscription is cancelled.",
        CheckInBlock.NotStarted => "Subscription has not started yet.",
        CheckInBlock.Expired => "Subscription has expired.",
        CheckInBlock.NoSessionsLeft => "No sessions left in this pack.",
        CheckInBlock.AlreadyCheckedInToday => "Already checked in today.",
        CheckInBlock.Paused => "Subscription is paused. Resume it first.",
        _ => block.ToString(),
    };
}
