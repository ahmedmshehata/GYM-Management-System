using GymPro.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace GymPro.Tests;

public class AuditTests
{
    [Fact]
    public async Task Insert_and_update_are_audited_with_user_and_old_new_values()
    {
        await using var t = await TestDb.CreateAsync();
        var m = await t.Members().SaveAsync(new Member { FullName = "أحمد", Phone = "0100" }, [0xFF, 0xD8, 1, 2, 3], TestContext.Current.CancellationToken);

        var edit = (await t.Members().GetAsync(m.Id, TestContext.Current.CancellationToken))!;
        edit.Phone = "0111";
        await t.Members().SaveAsync(edit, null, TestContext.Current.CancellationToken);

        await using var db = t.Context();
        var logs = await db.AuditLogs.OrderBy(a => a.Id).ToListAsync(TestContext.Current.CancellationToken);
        var insert = logs.Single(a => a.EntityType == nameof(Member) && a.Action == AuditAction.Insert);
        Assert.Equal(m.Id.ToString(), insert.EntityId);        // real key, not EF's temporary one
        Assert.Equal("tester", insert.UserName);
        Assert.Contains("أحمد", insert.Details);               // Arabic stays readable

        var photo = logs.Single(a => a.EntityType == nameof(MemberPhoto));
        Assert.Contains("***", photo.Details);                 // blob not dumped into the log

        var update = logs.Single(a => a.EntityType == nameof(Member) && a.Action == AuditAction.Update);
        Assert.Contains("\"old\":\"0100\"", update.Details);
        Assert.Contains("\"new\":\"0111\"", update.Details);
    }

    [Fact]
    public async Task Audit_log_cannot_be_modified_in_code_or_raw_sql()
    {
        await using var t = await TestDb.CreateAsync();
        await t.Plans().SaveAsync(new Plan { Name = "Month", DurationMonths = 1, Price = 100 }, TestContext.Current.CancellationToken);

        await using var db = t.Context();
        var log = await db.AuditLogs.FirstAsync(TestContext.Current.CancellationToken);
        log.Details = "tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));

        await using var raw = t.Context();
        await Assert.ThrowsAnyAsync<Exception>(() => raw.Database.ExecuteSqlRawAsync("DELETE FROM AuditLogs", TestContext.Current.CancellationToken));
    }
}

public class MultiUserTests
{
    [Fact]
    public async Task Second_user_editing_a_stale_copy_gets_a_concurrency_error()
    {
        await using var t = await TestDb.CreateAsync();
        var m = await t.Members().SaveAsync(new Member { FullName = "Sara" }, null, TestContext.Current.CancellationToken);

        var userA = (await t.Members().GetAsync(m.Id, TestContext.Current.CancellationToken))!;
        var userB = (await t.Members().GetAsync(m.Id, TestContext.Current.CancellationToken))!;
        userA.Phone = "111";
        await t.Members().SaveAsync(userA, null, TestContext.Current.CancellationToken);

        userB.Phone = "222";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => t.Members().SaveAsync(userB, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Check_in_twice_same_day_is_rejected_and_sessions_count_down()
    {
        await using var t = await TestDb.CreateAsync();
        await t.Plans().SaveAsync(new Plan { Name = "12 sessions", Kind = PlanKind.SessionPack, SessionCount = 12, DurationMonths = 1, Price = 300 }, TestContext.Current.CancellationToken);
        var plan = (await t.Plans().ListAsync(true, TestContext.Current.CancellationToken)).Single();
        var m = await t.Members().SaveAsync(new Member { FullName = "Omar" }, null, TestContext.Current.CancellationToken);
        var sub = await t.Subscriptions().SellAsync(m.Id, plan.Id, new DateOnly(2026, 9, 20), 300, null, TestContext.Current.CancellationToken);

        var state = await t.Subscriptions().CheckInAsync(sub.Id, TestContext.Current.CancellationToken);
        Assert.Equal(11, state.SessionsLeft);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => t.Subscriptions().CheckInAsync(sub.Id, TestContext.Current.CancellationToken));
        Assert.Contains("Already", ex.Message);
    }

    [Fact]
    public async Task Reception_cannot_manage_plans()
    {
        await using var t = await TestDb.CreateAsync(UserRole.Reception);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            t.Plans().SaveAsync(new Plan { Name = "x", Price = 1 }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Account_locks_after_repeated_wrong_passwords()
    {
        await using var t = await TestDb.CreateAsync();
        t.User.SignOut();
        await t.Auth().CreateFirstAdminAsync("admin", "Admin", "correct-horse", TestContext.Current.CancellationToken);

        for (var i = 0; i < 5; i++)
        {
            Assert.False((await t.Auth().LoginAsync("admin", "wrong", TestContext.Current.CancellationToken)).Succeeded);
        }

        var locked = await t.Auth().LoginAsync("ADMIN", "correct-horse", TestContext.Current.CancellationToken); // user names are case-insensitive
        Assert.False(locked.Succeeded);
        Assert.Contains("locked", locked.Error, StringComparison.OrdinalIgnoreCase);

        t.Time.Now = t.Time.Now.AddMinutes(16);
        Assert.True((await t.Auth().LoginAsync("admin", "correct-horse", TestContext.Current.CancellationToken)).Succeeded);

        await using var db = t.Context();
        Assert.Equal(6, await db.AuditLogs.CountAsync(a => a.Action == AuditAction.LoginFailed, TestContext.Current.CancellationToken));
    }
}

public class PreferenceTests
{
    [Fact]
    public async Task Theme_choice_is_saved_per_user_without_audit_noise()
    {
        await using var t = await TestDb.CreateAsync();
        t.User.SignOut();
        await t.Auth().CreateFirstAdminAsync("admin", "Admin", "correct-horse", TestContext.Current.CancellationToken);
        await t.Auth().LoginAsync("admin", "correct-horse", TestContext.Current.CancellationToken);
        int before;
        await using (var db = t.Context())
        {
            before = await db.AuditLogs.CountAsync(TestContext.Current.CancellationToken);
        }

        await t.Auth().SavePreferencesAsync("material-dark", useBrandAccent: false, TestContext.Current.CancellationToken);

        await using var check = t.Context();
        var user = await check.Users.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("material-dark", user.ThemeId);
        Assert.False(user.UseBrandAccent);
        Assert.Equal(before, await check.AuditLogs.CountAsync(TestContext.Current.CancellationToken));
    }
}

public class LegacyImportTests
{
    private static string Legacy(string name) => Path.Combine(AppContext.BaseDirectory, "Legacy", name);

    [Fact]
    public async Task Imports_v2_plans_with_arabic_names_and_session_packs()
    {
        await using var t = await TestDb.CreateAsync();
        var report = await t.Importer().ImportAsync(Legacy("v2_db_at.db"), ct: TestContext.Current.CancellationToken);

        Assert.Equal(10, report.PlansImported);
        var plans = await t.Plans().ListAsync(false, TestContext.Current.CancellationToken);
        var pack = plans.Single(p => p.LegacyId == 1);
        Assert.Equal(PlanKind.SessionPack, pack.Kind);
        Assert.Equal(12, pack.SessionCount);
        Assert.Equal(12, plans.Single(p => p.LegacyId == 9).DurationMonths); // "سنوي" = yearly
    }

    [Fact]
    public async Task Imports_members_with_base64_photo_subscriptions_attendance_and_is_idempotent()
    {
        await using var t = await TestDb.CreateAsync();
        var first = await t.Importer().ImportAsync(Legacy("database_db_at.db"), ct: TestContext.Current.CancellationToken);

        Assert.Equal(1, first.MembersImported);
        Assert.Equal(1, first.PhotosImported);
        Assert.Equal(1, first.SubscriptionsImported);
        Assert.Equal(1, first.AttendancesImported);

        await using (var db = t.Context())
        {
            var m = await db.Members.Include(x => x.Photo).SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(1, m.MemberNo);                 // legacy card number kept
            Assert.Equal(Gender.Male, m.Gender);
            var s = await db.Subscriptions.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(new DateOnly(2017, 1, 20), s.StartDate);
            Assert.Equal(200m, s.AmountPaid);
            Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == AuditAction.Import, TestContext.Current.CancellationToken));
        }

        var second = await t.Importer().ImportAsync(Legacy("database_db_at.db"), ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, second.MembersImported + second.PlansImported + second.SubscriptionsImported + second.AttendancesImported);
    }

    [Fact]
    public async Task Rejects_a_file_that_is_not_a_legacy_database()
    {
        await using var t = await TestDb.CreateAsync();
        var bogus = Path.Combine(Path.GetTempPath(), $"not-legacy-{Guid.NewGuid():N}.db");
        await File.WriteAllBytesAsync(bogus, [], TestContext.Current.CancellationToken); // empty SQLite file, no legacy tables

        await Assert.ThrowsAsync<InvalidDataException>(() => t.Importer().ImportAsync(bogus, ct: TestContext.Current.CancellationToken));
        File.Delete(bogus);
    }
}
