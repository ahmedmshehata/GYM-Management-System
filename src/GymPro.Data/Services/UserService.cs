using GymPro.Core.Entities;
using GymPro.Core.Security;
using Microsoft.EntityFrameworkCore;

namespace GymPro.Data.Services;

public sealed record UserRow(int Id, string UserName, string DisplayName, UserRole Role, bool IsActive, DateTime? LastLoginAtUtc, long Version);

public sealed class UserService(IDbContextFactory<GymDbContext> factory, ICurrentUser currentUser, AuthService auth)
{
    public async Task<List<UserRow>> ListAsync(CancellationToken ct = default)
    {
        Permissions.Demand(currentUser, Permission.ManageUsers);
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking()
            .OrderBy(u => u.UserName)
            .Select(u => new UserRow(u.Id, u.UserName, u.DisplayName, u.Role, u.IsActive, u.LastLoginAtUtc, u.Version))
            .ToListAsync(ct);
    }

    public async Task CreateAsync(string userName, string displayName, UserRole role, string initialPassword, CancellationToken ct = default)
    {
        Permissions.Demand(currentUser, Permission.ManageUsers);
        auth.ValidatePassword(initialPassword);
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("User name and display name are required.");
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var name = userName.Trim();
        if (await db.Users.AnyAsync(u => u.UserName == name, ct))
        {
            throw new InvalidOperationException($"User '{name}' already exists.");
        }

        db.Users.Add(new AppUser
        {
            UserName = name,
            DisplayName = displayName.Trim(),
            Role = role,
            PasswordHash = PasswordHasher.Hash(initialPassword),
            MustChangePassword = true,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(UserRow row, CancellationToken ct = default)
    {
        Permissions.Demand(currentUser, Permission.ManageUsers);
        await using var db = await factory.CreateDbContextAsync(ct);
        var user = await db.Users.SingleAsync(u => u.Id == row.Id, ct);
        db.Entry(user).Property(u => u.Version).OriginalValue = row.Version;

        var losesAdmin = user.Role == UserRole.Admin && user.IsActive && (row.Role != UserRole.Admin || !row.IsActive);
        if (losesAdmin && !await db.Users.AnyAsync(u => u.Id != user.Id && u.Role == UserRole.Admin && u.IsActive, ct))
        {
            throw new InvalidOperationException("At least one active administrator is required.");
        }

        if (user.Id == currentUser.UserId && !row.IsActive)
        {
            throw new InvalidOperationException("You cannot deactivate your own account.");
        }

        user.DisplayName = row.DisplayName.Trim();
        user.Role = row.Role;
        user.IsActive = row.IsActive;
        await db.SaveChangesAsync(ct);
    }

    public async Task ResetPasswordAsync(int userId, string newPassword, CancellationToken ct = default)
    {
        Permissions.Demand(currentUser, Permission.ManageUsers);
        auth.ValidatePassword(newPassword);
        await using var db = await factory.CreateDbContextAsync(ct);
        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        user.PasswordHash = PasswordHasher.Hash(newPassword);
        user.MustChangePassword = true;
        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        await db.SaveChangesAsync(ct);
    }
}
