using GymPro.Core.Entities;
using GymPro.Core.Security;
using Microsoft.EntityFrameworkCore;

namespace GymPro.Data.Services;

public sealed class PlanService(IDbContextFactory<GymDbContext> factory, ICurrentUser currentUser)
{
    public async Task<List<Plan>> ListAsync(bool activeOnly, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var q = db.Plans.AsNoTracking();
        if (activeOnly)
        {
            q = q.Where(p => p.IsActive);
        }

        return await q.OrderBy(p => p.Kind).ThenBy(p => p.DurationMonths).ThenBy(p => p.Name).ToListAsync(ct);
    }

    public async Task SaveAsync(Plan input, CancellationToken ct = default)
    {
        Permissions.Demand(currentUser, Permission.ManagePlans);
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            throw new ArgumentException("Plan name is required.");
        }

        if (input.DurationMonths < 1 || input.Price < 0 || (input.Kind == PlanKind.SessionPack && input.SessionCount is null or < 1))
        {
            throw new ArgumentException("Duration must be >= 1 month, price >= 0, and session packs need a session count.");
        }

        if (input.AllowPause && input.MaxPauseDays is < 1 or > 365)
        {
            throw new ArgumentException("Pause days must be between 1 and 365.");
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var plan = input.Id == 0 ? db.Plans.Add(new Plan { Name = input.Name }).Entity : await db.Plans.SingleAsync(p => p.Id == input.Id, ct);
        if (input.Id != 0)
        {
            db.Entry(plan).Property(p => p.Version).OriginalValue = input.Version;
        }

        plan.Name = input.Name.Trim();
        plan.Kind = input.Kind;
        plan.DurationMonths = input.DurationMonths;
        plan.SessionCount = input.Kind == PlanKind.SessionPack ? input.SessionCount : null;
        plan.SessionsExpire = input.Kind != PlanKind.SessionPack || input.SessionsExpire;
        plan.AllowPause = input.AllowPause;
        plan.MaxPauseDays = input.AllowPause ? input.MaxPauseDays : plan.MaxPauseDays;
        plan.Price = input.Price;
        plan.IsActive = input.IsActive;
        await db.SaveChangesAsync(ct);
    }
}
