using GymPro.Core.Configuration;
using Microsoft.EntityFrameworkCore;

namespace GymPro.Data;

public static class DbInitializer
{
    private static readonly string[] AllowedJournalModes = ["WAL", "DELETE", "TRUNCATE"];

    /// <summary>Creates the folder, applies migrations, sets journal mode and installs the audit-log guard triggers.</summary>
    public static async Task InitializeAsync(IDbContextFactory<GymDbContext> factory, DatabaseOptions options, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(DataServiceCollectionExtensions.ResolvePath(options));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);
        await ApplyPragmasAndTriggersAsync(db, options.JournalMode, ct);
    }

    internal static async Task ApplyPragmasAndTriggersAsync(GymDbContext db, string journalMode, CancellationToken ct = default)
    {
        var mode = AllowedJournalModes.FirstOrDefault(m => m.Equals(journalMode, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Database:JournalMode must be one of {string.Join(", ", AllowedJournalModes)}.");

        // WAL lets readers on other windows/users keep working while one user writes.
        // Value comes from the allow-list above, never from free text.
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync($"PRAGMA journal_mode={mode};", ct);
#pragma warning restore EF1002

        // Defence in depth: even raw SQL can't rewrite history.
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TRIGGER IF NOT EXISTS AuditLogs_NoUpdate BEFORE UPDATE ON AuditLogs
            BEGIN SELECT RAISE(ABORT, 'AuditLogs is append-only'); END;
            """, ct);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TRIGGER IF NOT EXISTS AuditLogs_NoDelete BEFORE DELETE ON AuditLogs
            BEGIN SELECT RAISE(ABORT, 'AuditLogs is append-only'); END;
            """, ct);
    }
}
