using GymPro.Core.Entities;
using GymPro.Core.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GymPro.Data.Services;

public sealed record MemberRow(
    int Id, int MemberNo, string FullName, string? Phone, Gender Gender, bool IsActive,
    DateOnly? ActiveUntil, bool HasOpenEnded, byte[]? Thumbnail)
{
    public string ActiveText => ActiveUntil?.ToString("yyyy-MM-dd") ?? (HasOpenEnded ? "No expiry" : "");
}

/// <summary>Image resizing, implemented by the UI layer (WPF imaging) so the data layer stays UI-free.</summary>
public interface IImageProcessor
{
    /// <summary>Re-encode as JPEG fitting inside <paramref name="maxPixels"/> (never upscales). Null = not a readable image.</summary>
    byte[]? ResizeJpeg(byte[] image, int maxPixels, int quality);
}

public static class PhotoSizes
{
    /// <summary>Stored member photos: plenty for a 112 px avatar on a 4K screen, ~50 KB each.</summary>
    public const int Photo = 600;
    public const int PhotoQuality = 85;

    /// <summary>List and grid thumbnails.</summary>
    public const int ThumbnailPixels = 160;
    public const int ThumbnailQuality = 80;

    public static byte[]? Thumbnail(this IImageProcessor p, byte[] image) => p.ResizeJpeg(image, ThumbnailPixels, ThumbnailQuality);
}

/// <summary>One page of the member list plus the total, for "load more as you scroll".</summary>
public sealed record MemberPage(IReadOnlyList<MemberRow> Rows, int Total);

public sealed class MemberService(
    IDbContextFactory<GymDbContext> factory,
    ICurrentUser currentUser,
    TimeProvider time,
    IImageProcessor? images = null)
{


    public async Task<List<MemberRow>> SearchAsync(string? text, int take = 200, CancellationToken ct = default) =>
        [.. (await SearchPageAsync(text, 0, take, ct)).Rows];

    /// <summary>
    /// Paged search ordered by member number. The UI loads the next page when the user scrolls to the bottom,
    /// so all members are reachable without loading thousands of rows (and thumbnails) at once.
    /// </summary>
    public async Task<MemberPage> SearchPageAsync(string? text, int skip, int take, CancellationToken ct = default)
    {
        Permissions.Demand(currentUser, Permission.ManageMembers);
        await using var db = await factory.CreateDbContextAsync(ct);
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var q = db.Members.AsNoTracking();
        var exactNo = -1;

        if (!string.IsNullOrWhiteSpace(text))
        {
            var t = text.Trim();
            q = int.TryParse(t, out exactNo)
                ? q.Where(m => m.MemberNo == exactNo || m.Phone!.Contains(t))
                : q.Where(m => EF.Functions.Like(m.FullName, $"%{t}%") || m.Phone!.Contains(t) || m.NationalId == t);
        }

        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(m => m.MemberNo == exactNo ? 0 : 1).ThenBy(m => m.MemberNo).Skip(skip).Take(take)
            .Select(m => new MemberRow(
                m.Id, m.MemberNo, m.FullName, m.Phone, m.Gender, m.IsActive,
                m.Subscriptions.Where(s => s.Status == SubscriptionStatus.Active && s.EndDate >= today)
                    .Max(s => s.EndDate),
                m.Subscriptions.Any(s => s.Status == SubscriptionStatus.Active && s.EndDate == null),
                m.Photo != null ? m.Photo.Thumbnail : null))
            .ToListAsync(ct);
        return new MemberPage(rows, total);
    }

    public async Task<Member?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Members.AsNoTracking().Include(m => m.Photo).SingleOrDefaultAsync(m => m.Id == id, ct);
    }

    public async Task<int> NextMemberNoAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return (await db.Members.MaxAsync(m => (int?)m.MemberNo, ct) ?? 0) + 1;
    }

    /// <summary>Insert (Id == 0) or update. Photo: null = unchanged, empty = remove, bytes = replace.</summary>
    public async Task<Member> SaveAsync(Member input, byte[]? photo, CancellationToken ct = default)
    {
        Permissions.Demand(currentUser, Permission.ManageMembers);
        if (string.IsNullOrWhiteSpace(input.FullName))
        {
            throw new ArgumentException("Full name is required.");
        }

        // Two receptionists can add members at the same moment; the unique index on MemberNo
        // turns that race into a retry instead of a duplicate card number.
        for (var attempt = 1; ; attempt++)
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            Member entity;
            if (input.Id == 0)
            {
                entity = new Member { FullName = input.FullName };
                db.Members.Add(entity);
                entity.MemberNo = input.MemberNo > 0 && attempt == 1
                    ? input.MemberNo
                    : (await db.Members.MaxAsync(m => (int?)m.MemberNo, ct) ?? 0) + 1;
            }
            else
            {
                entity = await db.Members.Include(m => m.Photo).SingleAsync(m => m.Id == input.Id, ct);
                db.Entry(entity).Property(m => m.Version).OriginalValue = input.Version;
                entity.MemberNo = input.MemberNo;
            }

            entity.FullName = input.FullName.Trim();
            entity.NationalId = Clean(input.NationalId);
            entity.Phone = Clean(input.Phone);
            entity.Job = Clean(input.Job);
            entity.Address = Clean(input.Address);
            entity.Gender = input.Gender;
            entity.HeightCm = input.HeightCm;
            entity.WeightKg = input.WeightKg;
            entity.Notes = Clean(input.Notes);
            entity.IsActive = input.IsActive;

            if (photo is { Length: > 0 })
            {
                entity.Photo ??= new MemberPhoto { Data = photo };
                entity.Photo.Data = photo;
                entity.Photo.Thumbnail = images?.Thumbnail(photo);
            }
            else if (photo is { Length: 0 } && entity.Photo is not null)
            {
                db.MemberPhotos.Remove(entity.Photo);
            }

            try
            {
                await db.SaveChangesAsync(ct);
                return entity;
            }
            catch (DbUpdateException ex) when (input.Id == 0 && attempt < 3 && IsUniqueViolation(ex))
            {
                // someone took the number; loop and pick the next one
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                throw new InvalidOperationException($"Member number {entity.MemberNo} is already used.", ex);
            }
        }
    }

    internal static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqliteException { SqliteErrorCode: 19 } s && s.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase);

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
