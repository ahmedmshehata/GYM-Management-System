using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using GymPro.Core.Entities;
using GymPro.Core.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GymPro.Data.Import;

public sealed class ImportReport
{
    public int PlansImported { get; set; }
    public int MembersImported { get; set; }
    public int PhotosImported { get; set; }
    public int SubscriptionsImported { get; set; }
    public int AttendancesImported { get; set; }
    public int Skipped => SkipReasons.Values.Sum();
    public List<string> Warnings { get; } = [];

    /// <summary>Why rows were skipped, with counts, so a big "Skipped" number is never a mystery.</summary>
    public SortedDictionary<string, int> SkipReasons { get; } = new(StringComparer.Ordinal);

    public void Skip(string reason) => SkipReasons[reason] = SkipReasons.GetValueOrDefault(reason) + 1;

    public override string ToString() =>
        $"Plans: {PlansImported}, Members: {MembersImported} (photos {PhotosImported}), " +
        $"Subscriptions: {SubscriptionsImported}, Attendance: {AttendancesImported}, Skipped: {Skipped}, Warnings: {Warnings.Count}" +
        (SkipReasons.Count == 0 ? "" : Environment.NewLine + "Skipped: " + string.Join("; ", SkipReasons.Select(r => $"{r.Key} = {r.Value}")));
}

/// <summary>
/// Imports the legacy JASystem SQLite schema (tables <c>roles</c>, <c>members</c>, <c>subscribe</c>, <c>Attendance</c>).
/// Idempotent: rows already imported (matched by <c>LegacyId</c>) are skipped, so it can be re-run safely.
/// </summary>
public sealed class LegacyImporter(IDbContextFactory<GymDbContext> factory, ICurrentUser currentUser)
{
    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.FFFFFFF", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.FFFFFFF",
        "M/d/yyyy", "M/d/yyyy h:mm:ss tt", "d/M/yyyy", "dd/MM/yyyy", "dd/MM/yyyy HH:mm:ss",
    ];

    public async Task<ImportReport> ImportAsync(string legacyDbPath, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        Permissions.Demand(currentUser, Permission.ImportLegacy);
        if (!File.Exists(legacyDbPath))
        {
            throw new FileNotFoundException("Legacy database not found.", legacyDbPath);
        }

        var legacyDir = Path.GetDirectoryName(Path.GetFullPath(legacyDbPath))!;
        progress?.Report("Reading legacy database...");
        var legacy = await ReadLegacyAsync(legacyDbPath, ct);
        var report = new ImportReport();

        await using var db = await factory.CreateDbContextAsync(ct);
        db.AuditEnabled = false; // one summary audit entry instead of thousands
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // ---- Plans (legacy "roles") ----
        progress?.Report("Importing plans...");
        var planMap = await db.Plans.Where(p => p.LegacyId != null).ToDictionaryAsync(p => p.LegacyId!.Value, ct);
        foreach (var r in legacy.Roles)
        {
            var id = Int(r, "role_id");
            if (id is null || planMap.ContainsKey(id.Value))
            {
                report.Skip(id is null ? "Plan: missing id" : "Plan: already imported");
                continue;
            }

            var isPack = string.Equals(Str(r, "role_type"), "C", StringComparison.OrdinalIgnoreCase);
            var count = Int(r, "role_count") ?? 0;
            var plan = new Plan
            {
                Name = Str(r, "role_name") ?? $"Plan {id}",
                Kind = isPack ? PlanKind.SessionPack : PlanKind.TimeBased,
                DurationMonths = isPack ? 1 : Math.Max(1, count),
                SessionCount = isPack ? (count > 0 ? count : 12) : null, // legacy hardcoded 12 sessions
                SessionsExpire = true, // legacy packs were valid for a limited time
                Price = (decimal)(Dbl(r, "role_amount") ?? 0),
                LegacyId = id,
            };
            db.Plans.Add(plan);
            planMap[id.Value] = plan;
            report.PlansImported++;
        }

        await db.SaveChangesAsync(ct);

        // ---- Members ----
        progress?.Report("Importing members...");
        var memberMap = await db.Members.Where(m => m.LegacyId != null).ToDictionaryAsync(m => m.LegacyId!.Value, ct);
        var usedNumbers = (await db.Members.Select(m => m.MemberNo).ToListAsync(ct)).ToHashSet();
        var nextNo = usedNumbers.Count == 0 ? 1 : usedNumbers.Max() + 1;
        foreach (var r in legacy.Members)
        {
            var id = Int(r, "ID");
            if (id is null || memberMap.ContainsKey(id.Value))
            {
                report.Skip(id is null ? "Member: missing id" : "Member: already imported");
                continue;
            }

            // Keep the old card number when it's free, so members keep their ID.
            var memberNo = id.Value > 0 && !usedNumbers.Contains(id.Value) ? id.Value : nextNo++;
            if (memberNo != id.Value)
            {
                report.Warnings.Add($"Member {id}: number already used, assigned {memberNo}.");
            }

            usedNumbers.Add(memberNo);
            nextNo = Math.Max(nextNo, memberNo + 1);

            var member = new Member
            {
                MemberNo = memberNo,
                FullName = Str(r, "FullName") ?? $"Member {id}",
                NationalId = Str(r, "n_ID"),
                Phone = Str(r, "Phone"),
                Job = Str(r, "Job"),
                Address = Str(r, "MemberAddress"),
                Gender = ParseGender(Str(r, "Gender")),
                HeightCm = Positive(Dbl(r, "Hieght")), // legacy column names are misspelled
                WeightKg = Positive(Dbl(r, "Wieght")),
                LegacyId = id,
            };

            if (LoadPhoto(Str(r, "ImagePath"), legacyDir, out var warning) is { } photo)
            {
                member.Photo = new MemberPhoto { Data = photo };
                report.PhotosImported++;
            }
            else if (warning is not null)
            {
                report.Warnings.Add($"Member {id}: {warning}");
            }

            db.Members.Add(member);
            memberMap[id.Value] = member;
            report.MembersImported++;
        }

        await db.SaveChangesAsync(ct);

        // ---- Subscriptions ----
        progress?.Report("Importing subscriptions...");
        var subMap = await db.Subscriptions.Where(s => s.LegacyId != null).ToDictionaryAsync(s => s.LegacyId!.Value, ct);
        foreach (var r in legacy.Subscriptions)
        {
            var id = Int(r, "subscribe_id");
            if (id is null || subMap.ContainsKey(id.Value))
            {
                report.Skip(id is null ? "Subscription: missing id" : "Subscription: already imported");
                continue;
            }

            var memberId = Int(r, "subscribe_mem");
            var planId = Int(r, "subscribe_role");
            var start = Date(r, "subscribe_f");
            var end = Date(r, "subscribe_t");
            string? problem =
                memberId is null || !memberMap.ContainsKey(memberId.Value) ? $"unknown member {memberId}"
                : planId is null || !planMap.ContainsKey(planId.Value) ? $"unknown plan {planId}"
                : start is null || end is null ? "missing start/end date"
                : end < start ? "end date before start date"
                : null;
            if (problem is not null)
            {
                report.Skip("Subscription: " + problem);
                report.Warnings.Add($"Subscription {id}: skipped, {problem}.");
                continue;
            }

            var plan = planMap[planId!.Value];
            var paid = (decimal)(Dbl(r, "subscribe_pay") ?? 0);
            var remaining = (decimal)(Dbl(r, "subscribe_rmind") ?? 0);
            var flag = Str(r, "subscribe_flag");
            var sub = new Subscription
            {
                MemberId = memberMap[memberId!.Value].Id,
                PlanId = plan.Id,
                StartDate = DateOnly.FromDateTime(start!.Value),
                EndDate = DateOnly.FromDateTime(end!.Value),
                SessionsAllowed = plan.Kind == PlanKind.SessionPack ? plan.SessionCount : null,
                Price = paid + Math.Max(0, remaining), // legacy stored "paid" and "remaining" separately
                AmountPaid = paid,
                Notes = flag is null or "T" ? null : $"Legacy flag: {flag}",
                LegacyId = id,
            };
            db.Subscriptions.Add(sub);
            subMap[id.Value] = sub;
            report.SubscriptionsImported++;
        }

        await db.SaveChangesAsync(ct);

        // ---- Attendance ----
        progress?.Report("Importing attendance...");
        var existingAtt = (await db.Attendances.Select(a => new { a.LegacyId, a.SubscriptionId, a.Date }).ToListAsync(ct));
        var seenLegacy = existingAtt.Where(a => a.LegacyId != null).Select(a => a.LegacyId!.Value).ToHashSet();
        var seenDay = existingAtt.Select(a => (a.SubscriptionId, a.Date)).ToHashSet();
        var subsByMember = subMap.Values.GroupBy(s => s.MemberId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var r in legacy.Attendances)
        {
            var id = Int(r, "Attend_ID");
            if (id is null || seenLegacy.Contains(id.Value))
            {
                report.Skip(id is null ? "Attendance: missing id" : "Attendance: already imported");
                continue;
            }

            var when = Date(r, "AttendDate");
            var memberLegacy = Int(r, "MemberID");
            if (when is null || memberLegacy is null || !memberMap.TryGetValue(memberLegacy.Value, out var member))
            {
                report.Skip("Attendance: unknown member or missing date");
                report.Warnings.Add($"Attendance {id}: skipped, unknown member or missing date.");
                continue;
            }

            var day = DateOnly.FromDateTime(when.Value);
            var sub = Int(r, "SubscribeID") is { } sid && subMap.TryGetValue(sid, out var s) ? s
                : subsByMember.GetValueOrDefault(member.Id)?.FirstOrDefault(x => x.StartDate <= day && day <= x.EndDate);
            if (sub is null)
            {
                report.Skip("Attendance: no subscription covers the date");
                report.Warnings.Add($"Attendance {id}: skipped, no subscription covers {day:yyyy-MM-dd}.");
                continue;
            }

            if (!seenDay.Add((sub.Id, day)))
            {
                report.Skip("Attendance: same-day duplicate check-in"); // the old app never blocked double check-ins
                continue;
            }

            db.Attendances.Add(new Attendance
            {
                MemberId = member.Id,
                SubscriptionId = sub.Id,
                Date = day,
                CheckedInAt = when.Value,
                LegacyId = id,
            });
            seenLegacy.Add(id.Value);
            report.AttendancesImported++;
        }

        await db.SaveChangesAsync(ct);

        string sha;
        await using (var fs = File.OpenRead(legacyDbPath))
        {
            sha = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct));
        }

        db.AddAuditEvent(AuditAction.Import, JsonSerializer.Serialize(new
        {
            Source = Path.GetFullPath(legacyDbPath),
            Sha256 = sha,
            Summary = report.ToString(),
            report.SkipReasons,
            Warnings = report.Warnings.Take(200),
        }, GymDbContext.AuditJson));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        progress?.Report("Done. " + report);
        return report;
    }

    // ------------------------------------------------------------------ reading

    private sealed record LegacyData(
        List<Dictionary<string, object?>> Roles,
        List<Dictionary<string, object?>> Members,
        List<Dictionary<string, object?>> Subscriptions,
        List<Dictionary<string, object?>> Attendances);

    private static async Task<LegacyData> ReadLegacyAsync(string path, CancellationToken ct)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        await using var conn = new SqliteConnection(cs);
        await conn.OpenAsync(ct);

        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                tables.Add(r.GetString(0));
            }
        }

        if (!tables.Contains("roles") && !tables.Contains("members"))
        {
            throw new InvalidDataException("This file is not a legacy GYM database (no 'roles' or 'members' table).");
        }

        async Task<List<Dictionary<string, object?>>> Read(string table)
        {
            var rows = new List<Dictionary<string, object?>>();
            if (!tables.TryGetValue(table, out var actual))
            {
                return rows;
            }

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT * FROM \"{actual.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < r.FieldCount; i++)
                {
                    row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
                }

                rows.Add(row);
            }

            return rows;
        }

        return new LegacyData(await Read("roles"), await Read("members"), await Read("subscribe"), await Read("Attendance"));
    }

    private static string? Str(Dictionary<string, object?> r, string col) =>
        r.GetValueOrDefault(col) is { } v && Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim() is { Length: > 0 } s ? s : null;

    private static int? Int(Dictionary<string, object?> r, string col) => r.GetValueOrDefault(col) switch
    {
        null => null,
        long l => (int)l,
        double d => (int)d,
        var v => int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null,
    };

    private static double? Dbl(Dictionary<string, object?> r, string col) => r.GetValueOrDefault(col) switch
    {
        null => null,
        long l => l,
        double d => d,
        var v => double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null,
    };

    private static double? Positive(double? v) => v > 0 ? v : null;

    /// <summary>Legacy dates arrive as ISO text, US/EG text, Julian day (REAL) or Unix seconds (INTEGER).</summary>
    internal static DateTime? ParseDate(object? v) => v switch
    {
        null => null,
        double julian => DateTime.FromOADate(julian - 2415018.5),
        long unix when unix > 100_000 => DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime,
        string s when DateTime.TryParseExact(s.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) => d,
        string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) => d,
        _ => null,
    };

    private static DateTime? Date(Dictionary<string, object?> r, string col) => ParseDate(r.GetValueOrDefault(col));

    private static Gender ParseGender(string? g) => g?.ToUpperInvariant() switch
    {
        "M" or "MALE" or "ذكر" => Gender.Male,
        "F" or "FEMALE" or "انثى" or "أنثى" => Gender.Female,
        _ => Gender.Unknown,
    };

    /// <summary>
    /// The legacy <c>ImagePath</c> column holds either a Base64 image (JASystem_ImageToDB) or a file path.
    /// </summary>
    internal static byte[]? LoadPhoto(string? value, string legacyDir, out string? warning)
    {
        warning = null;
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("/Images/", StringComparison.OrdinalIgnoreCase))
        {
            return null; // empty or the app's built-in placeholder
        }

        if (value.Length > 260)
        {
            try
            {
                var bytes = Convert.FromBase64String(string.Concat(value.Where(c => !char.IsWhiteSpace(c))));
                if (IsImage(bytes))
                {
                    return bytes;
                }

                warning = "photo data is not a recognised image.";
                return null;
            }
            catch (FormatException)
            {
                warning = "photo data is not valid Base64.";
                return null;
            }
        }

        string[] candidates = Path.IsPathRooted(value)
            ? [value, Path.Combine(legacyDir, Path.GetFileName(value)), Path.Combine(legacyDir, "Images", Path.GetFileName(value))]
            : [Path.Combine(legacyDir, value), Path.Combine(legacyDir, "Images", Path.GetFileName(value))];
        foreach (var file in candidates)
        {
            if (File.Exists(file))
            {
                var bytes = File.ReadAllBytes(file);
                if (IsImage(bytes))
                {
                    return bytes;
                }
            }
        }

        warning = $"photo file not found ({value}); copy the photos next to the legacy .db or into an 'Images' folder and re-run.";
        return null;
    }

    private static bool IsImage(byte[] b) => b.Length > 4 && (
        (b[0] == 0xFF && b[1] == 0xD8) ||                               // JPEG
        (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) || // PNG
        (b[0] == 0x42 && b[1] == 0x4D) ||                               // BMP
        (b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46));                // GIF
}
