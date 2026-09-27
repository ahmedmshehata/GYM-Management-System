namespace GymPro.Core.Entities;

public class AppUser : Entity
{
    public required string UserName { get; set; }
    public required string DisplayName { get; set; }

    [NotAudited]
    public required string PasswordHash { get; set; }

    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }

    /// <summary>Preferred UI theme id (see ThemeCatalog); null = the gym's default. Follows the user to any PC.</summary>
    public string? ThemeId { get; set; }

    /// <summary>Whether the theme's accent is replaced by the gym's brand colour.</summary>
    public bool UseBrandAccent { get; set; } = true;
}
