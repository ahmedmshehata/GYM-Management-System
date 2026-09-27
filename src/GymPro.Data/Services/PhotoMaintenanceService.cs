using GymPro.Core.Entities;
using GymPro.Core.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GymPro.Data.Services;

public sealed record PhotoStats(int Photos, int MissingThumbnails, long PhotoBytes, long DatabaseFileBytes);

public sealed record OptimizeReport(int Resized, int ThumbnailsCreated, int Unreadable, long BytesBefore, long BytesAfter,
    long FileBefore, long FileAfter, string BackupPath);

/// <summary>
/// Photo housekeeping: thumbnails for fast grids, and shrinking oversized legacy photos
/// (the old app stored full camera images, making the database ~1 GB).
/// </summary>
public sealed class PhotoMaintenanceService(IDbContextFactory<GymDbContext> factory, IImageProcessor images, ICurrentUser currentUser)
{
    private const int BatchSize = 50;

    public async Task<PhotoStats> GetStatsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var count = await db.MemberPhotos.CountAsync(ct);
        var missing = await db.MemberPhotos.CountAsync(p => p.Thumbnail == null, ct);
        var bytes = await db.Database.SqlQueryRaw<long>("SELECT COALESCE(SUM(length(Data)), 0) AS Value FROM MemberPhotos").SingleAsync(ct);
        return new PhotoStats(count, missing, bytes, FileSize(db));
    }

    /// <summary>Creates missing thumbnails only. Cheap; runs in the background after sign-in. Resumable.</summary>
    public async Task<int> BackfillThumbnailsAsync(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var done = 0;
        var failed = new HashSet<int>(); // undecodable photos: skip instead of retrying forever
        while (!ct.IsCancellationRequested)
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            db.AuditEnabled = false; // derived data, not a user change
            var batch = await db.MemberPhotos
                .Where(p => p.Thumbnail == null && !failed.Contains(p.MemberId))
                .OrderBy(p => p.MemberId)
                .Take(BatchSize)
                .ToListAsync(ct);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var photo in batch)
            {
                if (images.Thumbnail(photo.Data) is { } thumb)
                {
                    photo.Thumbnail = thumb;
                    done++;
                }
                else
                {
                    failed.Add(photo.MemberId);
                }
            }

            await db.SaveChangesAsync(ct);
            progress?.Report(done);
        }

        return done;
    }

    /// <summary>
    /// Backs the database up, re-encodes every photo larger than <see cref="PhotoSizes.Photo"/> px (never upscales,
    /// keeps the original if re-encoding wouldn't make it smaller), creates thumbnails, then compacts the file.
    /// Irreversible apart from the backup, so it's Admin-only.
    /// </summary>
    public async Task<OptimizeReport> OptimizePhotosAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        Permissions.Demand(currentUser, Permission.ImportLegacy);

        string backup;
        long fileBefore, bytesBefore;
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            fileBefore = FileSize(db);
            bytesBefore = await db.Database.SqlQueryRaw<long>("SELECT COALESCE(SUM(length(Data)), 0) AS Value FROM MemberPhotos").SingleAsync(ct);
            var dataSource = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;
            backup = Path.Combine(Path.GetDirectoryName(dataSource)!, $"gym-backup-before-photo-optimize-{DateTime.Now:yyyyMMdd-HHmmss}.db");
            progress?.Report($"Backing up the database to {backup} ...");
            await db.Database.ExecuteSqlAsync($"VACUUM INTO {backup}", ct); // consistent copy even while others read
        }

        int resized = 0, thumbs = 0, unreadable = 0, seen = 0, lastId = 0;
        while (!ct.IsCancellationRequested)
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            db.AuditEnabled = false;
            var batch = await db.MemberPhotos.Where(p => p.MemberId > lastId).OrderBy(p => p.MemberId).Take(BatchSize).ToListAsync(ct);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var photo in batch)
            {
                lastId = photo.MemberId;
                if (images.ResizeJpeg(photo.Data, PhotoSizes.Photo, PhotoSizes.PhotoQuality) is not { } small)
                {
                    unreadable++;
                    continue;
                }

                if (small.Length < photo.Data.Length)
                {
                    photo.Data = small;
                    resized++;
                }

                if (photo.Thumbnail is null && images.Thumbnail(photo.Data) is { } thumb)
                {
                    photo.Thumbnail = thumb;
                    thumbs++;
                }
            }

            await db.SaveChangesAsync(ct);
            seen += batch.Count;
            progress?.Report($"Processed {seen} photos, resized {resized} ...");
        }

        long bytesAfter, fileAfter;
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            bytesAfter = await db.Database.SqlQueryRaw<long>("SELECT COALESCE(SUM(length(Data)), 0) AS Value FROM MemberPhotos").SingleAsync(ct);
            progress?.Report("Compacting the database file (VACUUM) ...");
            db.Database.SetCommandTimeout(TimeSpan.FromMinutes(30));
            await db.Database.ExecuteSqlRawAsync("VACUUM", ct); // SQLite only shrinks the file when vacuumed
            await db.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE)", ct); // in WAL mode the shrink lands on checkpoint
            fileAfter = FileSize(db);
            db.AddAuditEvent(AuditAction.Update, $"Photos optimized: {resized} resized, {thumbs} thumbnails, {bytesBefore:N0} -> {bytesAfter:N0} bytes; backup {backup}",
                entityType: nameof(MemberPhoto));
            await db.SaveChangesAsync(ct);
        }

        var report = new OptimizeReport(resized, thumbs, unreadable, bytesBefore, bytesAfter, fileBefore, fileAfter, backup);
        progress?.Report("Done.");
        return report;
    }

    private static long FileSize(GymDbContext db)
    {
        var path = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;
        return File.Exists(path) ? new FileInfo(path).Length : 0;
    }
}
