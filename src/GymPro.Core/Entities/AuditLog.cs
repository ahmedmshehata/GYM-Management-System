namespace GymPro.Core.Entities;

/// <summary>Append-only record of who changed what. Update/delete is blocked in code and by DB triggers.</summary>
public class AuditLog
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public int? UserId { get; set; }
    public string? UserName { get; set; }
    public AuditAction Action { get; set; }
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }

    /// <summary>JSON: <c>{"Prop": {"old": .., "new": ..}}</c> for updates, full values for insert/delete, free text for events.</summary>
    public string? Details { get; set; }

    public string? MachineName { get; set; }
}
