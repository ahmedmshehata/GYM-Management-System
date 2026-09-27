namespace GymPro.Core.Entities;

public class Attendance : Entity
{
    public int MemberId { get; set; }
    public Member? Member { get; set; }

    public int SubscriptionId { get; set; }
    public Subscription? Subscription { get; set; }

    /// <summary>Gym-local calendar day; unique per subscription so a member can't be checked in twice a day.</summary>
    public DateOnly Date { get; set; }

    public DateTime CheckedInAt { get; set; }
    public int? RecordedByUserId { get; set; }
    public int? LegacyId { get; set; }
}
