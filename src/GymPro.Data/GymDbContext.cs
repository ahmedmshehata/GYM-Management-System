using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using GymPro.Core.Entities;
using GymPro.Core.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace GymPro.Data;

public class GymDbContext(DbContextOptions<GymDbContext> options, ICurrentUser? currentUser = null) : DbContext(options)
{
    internal static readonly JsonSerializerOptions AuditJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep Arabic names readable in the log
    };

    private static readonly HashSet<string> NoiseProperties = [nameof(Entity.Version), nameof(Entity.UpdatedAtUtc), nameof(Entity.CreatedAtUtc)];

    public DbSet<Member> Members => Set<Member>();
    public DbSet<MemberPhoto> MemberPhotos => Set<MemberPhoto>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Attendance> Attendances => Set<Attendance>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    /// <summary>Turn off per-row auditing (bulk import writes one summary entry instead).</summary>
    public bool AuditEnabled { get; set; } = true;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var b = modelBuilder;

        b.Entity<Member>(e =>
        {
            e.Property(x => x.FullName).HasMaxLength(200);
            e.Property(x => x.NationalId).HasMaxLength(50);
            e.Property(x => x.Phone).HasMaxLength(50);
            e.HasIndex(x => x.MemberNo).IsUnique();
            e.HasIndex(x => x.FullName);
            e.HasIndex(x => x.Phone);
            e.HasIndex(x => x.LegacyId).IsUnique().HasFilter("LegacyId IS NOT NULL");
            e.HasOne(x => x.Photo).WithOne().HasForeignKey<MemberPhoto>(p => p.MemberId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<MemberPhoto>().HasKey(x => x.MemberId);

        b.Entity<Plan>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.SessionsExpire).HasDefaultValue(true);
            e.Property(x => x.MaxPauseDays).HasDefaultValue(30);
            e.HasIndex(x => x.LegacyId).IsUnique().HasFilter("LegacyId IS NOT NULL");
        });

        b.Entity<Subscription>(e =>
        {
            e.Ignore(x => x.Balance);
            e.HasOne(x => x.Member).WithMany(m => m.Subscriptions).HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Plan).WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.MemberId, x.EndDate });
            e.HasIndex(x => x.LegacyId).IsUnique().HasFilter("LegacyId IS NOT NULL");
        });

        b.Entity<Attendance>(e =>
        {
            e.HasOne(x => x.Member).WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Subscription).WithMany(s => s.Attendances).HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.SubscriptionId, x.Date }).IsUnique(); // one check-in per day per subscription
            e.HasIndex(x => new { x.MemberId, x.Date });
            e.HasIndex(x => x.LegacyId).IsUnique().HasFilter("LegacyId IS NOT NULL");
        });

        b.Entity<AppUser>(e =>
        {
            e.Property(x => x.UserName).HasMaxLength(50).UseCollation("NOCASE");
            e.HasIndex(x => x.UserName).IsUnique();
            e.Property(x => x.DisplayName).HasMaxLength(100);
            e.Property(x => x.ThemeId).HasMaxLength(40);
            e.Property(x => x.UseBrandAccent).HasDefaultValue(true);
        });

        b.Entity<AuditLog>(e =>
        {
            e.HasIndex(x => x.TimestampUtc);
            e.HasIndex(x => new { x.EntityType, x.EntityId });
            e.HasIndex(x => x.UserId);
            e.Property(x => x.EntityType).HasMaxLength(50);
            e.Property(x => x.EntityId).HasMaxLength(50);
            e.Property(x => x.UserName).HasMaxLength(50);
            e.Property(x => x.MachineName).HasMaxLength(100);
        });

        foreach (var type in b.Model.GetEntityTypes().Where(t => typeof(Entity).IsAssignableFrom(t.ClrType)))
        {
            b.Entity(type.ClrType).Property(nameof(Entity.Version)).IsConcurrencyToken();
        }
    }

    /// <summary>Record a non-entity event (login, logout, import...). Saved with the next SaveChanges.</summary>
    public void AddAuditEvent(AuditAction action, string? details, int? userId = null, string? userName = null, string? entityType = null, string? entityId = null)
    {
        AuditLogs.Add(new AuditLog
        {
            TimestampUtc = DateTime.UtcNow,
            UserId = userId ?? currentUser?.UserId,
            UserName = userName ?? currentUser?.UserName,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
            MachineName = Environment.MachineName,
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var pending = PrepareChanges();
        if (pending.Count == 0)
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        using var ownTx = Database.CurrentTransaction is null ? Database.BeginTransaction() : null;
        var result = base.SaveChanges(acceptAllChangesOnSuccess);
        AuditLogs.AddRange(pending.Select(p => p.Complete()));
        base.SaveChanges(acceptAllChangesOnSuccess);
        ownTx?.Commit();
        return result;
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var pending = PrepareChanges();
        if (pending.Count == 0)
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        // Entity rows and their audit rows commit together or not at all.
        await using var ownTx = Database.CurrentTransaction is null ? await Database.BeginTransactionAsync(cancellationToken) : null;
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        AuditLogs.AddRange(pending.Select(p => p.Complete()));
        await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        if (ownTx is not null)
        {
            await ownTx.CommitAsync(cancellationToken);
        }

        return result;
    }

    private List<PendingAudit> PrepareChanges()
    {
        var now = DateTime.UtcNow;
        var pending = new List<PendingAudit>();

        foreach (var entry in ChangeTracker.Entries().ToList())
        {
            if (entry.Entity is AuditLog)
            {
                if (entry.State is EntityState.Modified or EntityState.Deleted)
                {
                    throw new InvalidOperationException("The audit log is append-only.");
                }

                continue;
            }

            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (AuditEnabled && BuildAudit(entry, now) is { } audit)
            {
                pending.Add(audit);
            }

            if (entry.Entity is Entity entity)
            {
                if (entry.State == EntityState.Added)
                {
                    entity.CreatedAtUtc = now;
                    entity.Version = 1;
                }
                else if (entry.State == EntityState.Modified)
                {
                    entity.UpdatedAtUtc = now;
                    entity.Version++; // original value stays in the WHERE clause -> DbUpdateConcurrencyException on conflict
                }
            }
        }

        return pending;
    }

    private PendingAudit? BuildAudit(EntityEntry entry, DateTime now)
    {
        var details = new Dictionary<string, object?>();
        foreach (var p in entry.Properties)
        {
            var name = p.Metadata.Name;
            if (NoiseProperties.Contains(name))
            {
                continue;
            }

            var hidden = p.Metadata.PropertyInfo?.GetCustomAttribute<NotAuditedAttribute>() is not null;
            switch (entry.State)
            {
                case EntityState.Added:
                    details[name] = hidden ? "***" : p.CurrentValue;
                    break;
                case EntityState.Deleted:
                    details[name] = hidden ? "***" : p.OriginalValue;
                    break;
                case EntityState.Modified when p.IsModified && !Equals(p.OriginalValue, p.CurrentValue):
                    details[name] = hidden ? new { changed = true } : new { old = p.OriginalValue, @new = p.CurrentValue };
                    break;
            }
        }

        if (entry.State == EntityState.Modified && details.Count == 0)
        {
            return null;
        }

        var log = new AuditLog
        {
            TimestampUtc = now,
            UserId = currentUser?.UserId,
            UserName = currentUser?.UserName,
            Action = entry.State switch
            {
                EntityState.Added => AuditAction.Insert,
                EntityState.Deleted => AuditAction.Delete,
                _ => AuditAction.Update,
            },
            EntityType = entry.Metadata.ClrType.Name,
            Details = JsonSerializer.Serialize(details, AuditJson),
            MachineName = Environment.MachineName,
        };
        return new PendingAudit(entry, log);
    }

    private sealed record PendingAudit(EntityEntry Entry, AuditLog Log)
    {
        /// <summary>Called after the first save so database-generated keys are known.</summary>
        public AuditLog Complete()
        {
            var key = Entry.Metadata.FindPrimaryKey()!;
            Log.EntityId = string.Join(",", key.Properties.Select(k => Entry.Property(k.Name).CurrentValue));
            return Log;
        }
    }
}
