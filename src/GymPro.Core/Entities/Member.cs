namespace GymPro.Core.Entities;

public class Member : Entity
{
    /// <summary>Human-facing membership number printed on cards. Legacy IDs are preserved here on import.</summary>
    public int MemberNo { get; set; }

    public required string FullName { get; set; }
    public string? NationalId { get; set; }
    public string? Phone { get; set; }
    public string? Job { get; set; }
    public string? Address { get; set; }
    public Gender Gender { get; set; }
    public double? HeightCm { get; set; }
    public double? WeightKg { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Legacy <c>members.ID</c>, set only for imported rows; makes re-import idempotent.</summary>
    public int? LegacyId { get; set; }

    public MemberPhoto? Photo { get; set; }
    public List<Subscription> Subscriptions { get; set; } = [];
}

/// <summary>Photo lives in its own table so member lists never load image blobs.</summary>
public class MemberPhoto
{
    public int MemberId { get; set; }

    [NotAudited]
    public required byte[] Data { get; set; }

    /// <summary>Small JPEG (about 160 px) for lists and grids, so browsing never loads full-size photos.</summary>
    [NotAudited]
    public byte[]? Thumbnail { get; set; }
}
