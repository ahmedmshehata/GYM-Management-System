namespace GymPro.Core.Entities;

/// <summary>Base for every audited, concurrency-checked row.</summary>
public abstract class Entity
{
    public int Id { get; set; }

    /// <summary>Optimistic-concurrency token; bumped on every update so two users can't silently overwrite each other.</summary>
    public long Version { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

/// <summary>Excludes a property's values from the audit log (only "changed" is recorded).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NotAuditedAttribute : Attribute;
