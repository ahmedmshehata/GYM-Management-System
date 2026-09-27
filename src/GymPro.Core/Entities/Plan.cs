namespace GymPro.Core.Entities;

/// <summary>A sellable membership plan (legacy table <c>roles</c>).</summary>
public class Plan : Entity
{
    public required string Name { get; set; }
    public PlanKind Kind { get; set; }

    /// <summary>How long the plan is valid, in months.</summary>
    public int DurationMonths { get; set; } = 1;

    /// <summary>Sessions included; only meaningful for <see cref="PlanKind.SessionPack"/>.</summary>
    public int? SessionCount { get; set; }

    /// <summary>
    /// Session packs only. True = the sessions must be used within <see cref="DurationMonths"/>;
    /// false = no expiry date, the pack simply counts down per session across any months.
    /// </summary>
    public bool SessionsExpire { get; set; } = true;

    /// <summary>Whether a sold subscription can be paused (frozen), e.g. for travel or injury.</summary>
    public bool AllowPause { get; set; }

    /// <summary>Total days a subscription of this plan may be paused, across all pauses.</summary>
    public int MaxPauseDays { get; set; } = 30;

    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
    public int? LegacyId { get; set; }
}
