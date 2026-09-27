using GymPro.Core.Entities;

namespace GymPro.Core.Security;

/// <summary>The signed-in operator. Read by the DbContext to stamp audit rows.</summary>
public interface ICurrentUser
{
    int? UserId { get; }
    string? UserName { get; }
    UserRole? Role { get; }
    bool IsAuthenticated => UserId is not null;
}

public sealed class CurrentUser : ICurrentUser
{
    public int? UserId { get; private set; }
    public string? UserName { get; private set; }
    public UserRole? Role { get; private set; }

    public void SignIn(AppUser user)
    {
        UserId = user.Id;
        UserName = user.UserName;
        Role = user.Role;
    }

    public void SignOut()
    {
        UserId = null;
        UserName = null;
        Role = null;
    }
}

/// <summary>What each role may do. Roles are cumulative: Admin ⊃ Manager ⊃ Reception.</summary>
public enum Permission
{
    CheckIn,
    ManageMembers,
    SellSubscriptions,
    ManagePlans,
    ViewAudit,
    ManageUsers,
    ImportLegacy,
}

public static class Permissions
{
    public static bool Has(UserRole? role, Permission permission) => role is { } r && permission switch
    {
        Permission.CheckIn or Permission.ManageMembers or Permission.SellSubscriptions => true,
        Permission.ManagePlans or Permission.ViewAudit => r >= UserRole.Manager,
        Permission.ManageUsers or Permission.ImportLegacy => r >= UserRole.Admin,
        _ => false,
    };

    public static void Demand(ICurrentUser user, Permission permission)
    {
        if (!Has(user.Role, permission))
        {
            throw new UnauthorizedAccessException($"Your role is not allowed to: {permission}.");
        }
    }
}
