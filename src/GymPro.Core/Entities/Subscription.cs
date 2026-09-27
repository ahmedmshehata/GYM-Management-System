namespace GymPro.Core.Entities;

public class Subscription : Entity
{
    public int MemberId { get; set; }
    public Member? Member { get; set; }

    public int PlanId { get; set; }
    public Plan? Plan { get; set; }

    public DateOnly StartDate { get; set; }

    /// <summary>Last valid day; null = no expiry (session pack counted per session only).</summary>
    public DateOnly? EndDate { get; set; }

    /// <summary>Snapshot of the plan's pause allowance at sale time; 0 = pausing not allowed.</summary>
    public int PauseDaysAllowed { get; set; }

    /// <summary>Days already used by finished pauses (each one also pushed <see cref="EndDate"/> back).</summary>
    public int PausedDays { get; set; }

    /// <summary>Set while the subscription is paused: the first paused day.</summary>
    public DateOnly? PausedFrom { get; set; }

    /// <summary>Snapshot of the plan's session count at sale time; null = unlimited.</summary>
    public int? SessionsAllowed { get; set; }

    /// <summary>Snapshot of the price at sale time, so later price changes don't rewrite history.</summary>
    public decimal Price { get; set; }

    public decimal AmountPaid { get; set; }
    public decimal Balance => Price - AmountPaid;

    public SubscriptionStatus Status { get; set; }
    public string? Notes { get; set; }
    public int? LegacyId { get; set; }

    public List<Attendance> Attendances { get; set; } = [];
}
