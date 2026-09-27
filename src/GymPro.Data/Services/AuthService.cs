using GymPro.Core.Configuration;
using GymPro.Core.Entities;
using GymPro.Core.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GymPro.Data.Services;

public sealed record LoginResult(bool Succeeded, string? Error, AppUser? User)
{
    public static LoginResult Fail(string error) => new(false, error, null);
}

public sealed class AuthService(
    IDbContextFactory<GymDbContext> factory,
    CurrentUser currentUser,
    IOptions<SecurityOptions> security,
    TimeProvider time)
{
    // Hash of a random password; verifying against it when the user doesn't exist keeps timing uniform.
    private static readonly string DummyHash = PasswordHasher.Hash(Guid.NewGuid().ToString());

    public async Task<bool> HasAnyUserAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Users.AnyAsync(ct);
    }

    /// <summary>First-run only: creates the initial administrator. Refuses once any user exists.</summary>
    public async Task<AppUser> CreateFirstAdminAsync(string userName, string displayName, string password, CancellationToken ct = default)
    {
        ValidatePassword(password);
        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.Users.AnyAsync(ct))
        {
            throw new InvalidOperationException("An administrator already exists.");
        }

        var user = new AppUser
        {
            UserName = userName.Trim(),
            DisplayName = displayName.Trim(),
            PasswordHash = PasswordHasher.Hash(password),
            Role = UserRole.Admin,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task<LoginResult> LoginAsync(string userName, string password, CancellationToken ct = default)
    {
        var opts = security.Value;
        var now = time.GetUtcNow().UtcDateTime;
        await using var db = await factory.CreateDbContextAsync(ct);
        db.AuditEnabled = false; // counters/last-login bookkeeping is noise; the explicit Login/LoginFailed events below are the record
        var name = userName.Trim();
        var user = await db.Users.FirstOrDefaultAsync(u => u.UserName == name, ct);

        if (user is null || !user.IsActive)
        {
            PasswordHasher.Verify(password, DummyHash);
            db.AddAuditEvent(AuditAction.LoginFailed, user is null ? "Unknown user" : "Inactive user", user?.Id, name);
            await db.SaveChangesAsync(ct);
            return LoginResult.Fail("Invalid user name or password.");
        }

        if (user.LockoutEndUtc > now)
        {
            db.AddAuditEvent(AuditAction.LoginFailed, "Locked out", user.Id, user.UserName);
            await db.SaveChangesAsync(ct);
            return LoginResult.Fail($"Account locked. Try again after {user.LockoutEndUtc.Value.ToLocalTime():t}.");
        }

        if (!PasswordHasher.Verify(password, user.PasswordHash))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= opts.MaxFailedLogins)
            {
                user.LockoutEndUtc = now.AddMinutes(opts.LockoutMinutes);
                user.FailedLoginCount = 0;
            }

            db.AddAuditEvent(AuditAction.LoginFailed, user.LockoutEndUtc > now ? "Wrong password; account locked" : "Wrong password", user.Id, user.UserName);
            await db.SaveChangesAsync(ct);
            return LoginResult.Fail("Invalid user name or password.");
        }

        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        user.LastLoginAtUtc = now;
        db.AddAuditEvent(AuditAction.Login, null, user.Id, user.UserName);
        await db.SaveChangesAsync(ct);

        currentUser.SignIn(user);
        return new LoginResult(true, null, user);
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        if (currentUser.UserId is null)
        {
            return;
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        db.AddAuditEvent(AuditAction.Logout, null);
        await db.SaveChangesAsync(ct);
        currentUser.SignOut();
    }

    public async Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken ct = default)
    {
        ValidatePassword(newPassword);
        await using var db = await factory.CreateDbContextAsync(ct);
        var user = await db.Users.SingleAsync(u => u.Id == currentUser.UserId, ct);
        if (!PasswordHasher.Verify(currentPassword, user.PasswordHash))
        {
            throw new InvalidOperationException("Current password is incorrect.");
        }

        user.PasswordHash = PasswordHasher.Hash(newPassword);
        user.MustChangePassword = false;
        db.AddAuditEvent(AuditAction.PasswordChanged, null, entityType: nameof(AppUser), entityId: user.Id.ToString());
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Saves the signed-in user's appearance choice. Not audited: it's cosmetic and would flood the log.</summary>
    public async Task SavePreferencesAsync(string themeId, bool useBrandAccent, CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } id)
        {
            return;
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        db.AuditEnabled = false;
        var user = await db.Users.SingleAsync(u => u.Id == id, ct);
        user.ThemeId = themeId;
        user.UseBrandAccent = useBrandAccent;
        await db.SaveChangesAsync(ct);
    }

    public void ValidatePassword(string password)
    {
        var min = security.Value.MinPasswordLength;
        if (string.IsNullOrWhiteSpace(password) || password.Length < min)
        {
            throw new ArgumentException($"Password must be at least {min} characters.");
        }
    }
}
