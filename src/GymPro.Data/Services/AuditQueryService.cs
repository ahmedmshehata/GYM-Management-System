using GymPro.Core.Entities;
using GymPro.Core.Security;
using Microsoft.EntityFrameworkCore;

namespace GymPro.Data.Services;

public sealed record AuditFilter(DateOnly From, DateOnly To, string? UserName, string? EntityType, AuditAction? Action, string? Text);

public sealed class AuditQueryService(IDbContextFactory<GymDbContext> factory, ICurrentUser currentUser)
{
    public async Task<List<AuditLog>> QueryAsync(AuditFilter f, int take = 1000, CancellationToken ct = default)
    {
        Permissions.Demand(currentUser, Permission.ViewAudit);
        await using var db = await factory.CreateDbContextAsync(ct);

        var fromUtc = f.From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
        var toUtc = f.To.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
        var q = db.AuditLogs.AsNoTracking().Where(a => a.TimestampUtc >= fromUtc && a.TimestampUtc < toUtc);

        if (!string.IsNullOrWhiteSpace(f.UserName))
        {
            q = q.Where(a => a.UserName == f.UserName);
        }

        if (!string.IsNullOrWhiteSpace(f.EntityType))
        {
            q = q.Where(a => a.EntityType == f.EntityType);
        }

        if (f.Action is { } action)
        {
            q = q.Where(a => a.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(f.Text))
        {
            q = q.Where(a => a.Details!.Contains(f.Text) || a.EntityId == f.Text);
        }

        return await q.OrderByDescending(a => a.Id).Take(take).ToListAsync(ct);
    }

    /// <summary>Full history for one row, e.g. everything that ever happened to Member 42.</summary>
    public async Task<List<AuditLog>> HistoryAsync(string entityType, int id, CancellationToken ct = default)
    {
        Permissions.Demand(currentUser, Permission.ViewAudit);
        await using var db = await factory.CreateDbContextAsync(ct);
        var key = id.ToString();
        return await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == entityType && a.EntityId == key)
            .OrderByDescending(a => a.Id)
            .ToListAsync(ct);
    }
}
