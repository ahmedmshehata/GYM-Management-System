using GymPro.Core.Entities;
using GymPro.Core.Rules;
using GymPro.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace GymPro.Tests;

public class PauseRulesTests
{
    private static readonly DateOnly Day0 = new(2026, 9, 1);

    private static Subscription Monthly(int pauseDays = 10) => new()
    {
        StartDate = Day0,
        EndDate = Day0.AddDays(29),
        PauseDaysAllowed = pauseDays,
    };

    [Fact]
    public void Paused_subscription_blocks_check_in_and_freezes_days_left()
    {
        var s = Monthly();
        s.PausedFrom = Day0.AddDays(10);
        var at10 = MembershipRules.Evaluate(s, 0, false, Day0.AddDays(10));
        var at14 = MembershipRules.Evaluate(s, 0, false, Day0.AddDays(14));

        Assert.Equal(CheckInBlock.Paused, at14.Block);
        Assert.Equal(at10.DaysLeft, at14.DaysLeft); // the clock stopped
    }

    [Fact]
    public void Resume_pushes_end_date_back_by_the_paused_days()
    {
        var s = Monthly();
        s.PausedFrom = Day0.AddDays(10);
        var added = MembershipRules.EndPause(s, Day0.AddDays(14));

        Assert.Equal(4, added);
        Assert.Equal(Day0.AddDays(33), s.EndDate);
        Assert.Equal(4, s.PausedDays);
        Assert.Null(s.PausedFrom);
    }

    [Fact]
    public void Pause_that_outlives_its_allowance_ends_by_itself()
    {
        var s = Monthly(pauseDays: 5);
        s.PausedFrom = Day0.AddDays(10);
        var state = MembershipRules.Evaluate(s, 0, false, Day0.AddDays(20)); // 10 days later, only 5 allowed

        Assert.Equal(CheckInBlock.None, state.Block);
        Assert.Equal(Day0.AddDays(34), state.EffectiveEnd); // extended by the 5 allowed days only
        Assert.Equal(5, MembershipRules.EndPause(s, Day0.AddDays(20)));
        Assert.Equal("No pause days left on this subscription.", MembershipRules.CannotPause(s, Day0.AddDays(20)));
    }

    [Fact]
    public void Plans_without_pause_cannot_be_paused() =>
        Assert.Equal("This plan doesn't allow pausing.", MembershipRules.CannotPause(Monthly(pauseDays: 0), Day0));

    [Fact]
    public void Non_expiring_session_pack_has_no_end_date_and_counts_sessions_only()
    {
        var plan = new Plan { Name = "10 sessions", Kind = PlanKind.SessionPack, SessionCount = 10, SessionsExpire = false };
        Assert.Null(MembershipRules.EndDateFor(plan, Day0));

        var s = new Subscription { StartDate = Day0, EndDate = null, SessionsAllowed = 10 };
        var twoYearsLater = MembershipRules.Evaluate(s, 9, false, Day0.AddYears(2));
        Assert.True(twoYearsLater.CanCheckIn);
        Assert.Null(twoYearsLater.DaysLeft);
        Assert.Equal(1, twoYearsLater.SessionsLeft);
        Assert.Equal(CheckInBlock.NoSessionsLeft, MembershipRules.Evaluate(s, 10, false, Day0.AddYears(2)).Block);
    }
}

public class PauseServiceTests
{
    [Fact]
    public async Task Pause_then_resume_through_the_service_extends_the_membership()
    {
        await using var t = await TestDb.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        await t.Plans().SaveAsync(new Plan { Name = "Month", DurationMonths = 1, Price = 400, AllowPause = true, MaxPauseDays = 14 }, ct);
        var plan = (await t.Plans().ListAsync(true, ct)).Single();
        var m = await t.Members().SaveAsync(new Member { FullName = "Hana" }, null, ct);
        var sub = await t.Subscriptions().SellAsync(m.Id, plan.Id, new DateOnly(2026, 9, 20), 400, null, ct);
        Assert.Equal(14, sub.PauseDaysAllowed);

        await t.Subscriptions().PauseAsync(sub.Id, sub.Version, ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => t.Subscriptions().CheckInAsync(sub.Id, ct));

        t.Time.Now = t.Time.Now.AddDays(3);
        var row = (await t.Subscriptions().ListForMemberAsync(m.Id, ct)).Single();
        Assert.True(row.IsPaused);
        Assert.Equal(3, await t.Subscriptions().ResumeAsync(sub.Id, row.Version, ct));

        await using var db = t.Context();
        var saved = await db.Subscriptions.SingleAsync(ct);
        Assert.Equal(new DateOnly(2026, 10, 19).AddDays(3), saved.EndDate);
        Assert.NotNull(await t.Subscriptions().CheckInAsync(sub.Id, ct));
    }

    [Fact]
    public async Task Selling_a_non_expiring_pack_stores_no_end_date()
    {
        await using var t = await TestDb.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        await t.Plans().SaveAsync(new Plan { Name = "Flex 10", Kind = PlanKind.SessionPack, SessionCount = 10, SessionsExpire = false, Price = 250 }, ct);
        var plan = (await t.Plans().ListAsync(true, ct)).Single();
        var m = await t.Members().SaveAsync(new Member { FullName = "Karim" }, null, ct);
        var sub = await t.Subscriptions().SellAsync(m.Id, plan.Id, new DateOnly(2026, 1, 1), 250, null, ct);

        Assert.Null(sub.EndDate);
        var row = (await t.Subscriptions().ListForMemberAsync(m.Id, ct)).Single();
        Assert.Equal("No expiry", row.EndsText);
        Assert.Equal("10 sessions", row.Remaining);
        Assert.True(row.State.CanCheckIn); // bought in January, used in September
    }
}

public class MemberPagingTests
{
    [Fact]
    public async Task Pages_cover_every_member_and_exact_number_comes_first()
    {
        await using var t = await TestDb.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        await using (var db = t.Context())
        {
            db.AuditEnabled = false;
            for (var i = 1; i <= 250; i++)
            {
                db.Members.Add(new Member { MemberNo = i, FullName = $"Member {i}", Phone = $"0100{i:0000000}" });
            }

            await db.SaveChangesAsync(ct);
        }

        var seen = new List<int>();
        for (var skip = 0; ; skip += 100)
        {
            var page = await t.Members().SearchPageAsync(null, skip, 100, ct);
            Assert.Equal(250, page.Total);
            if (page.Rows.Count == 0)
            {
                break;
            }

            seen.AddRange(page.Rows.Select(r => r.MemberNo));
        }

        Assert.Equal(Enumerable.Range(1, 250), seen);

        // "12" also matches many phone numbers, but card no. 12 must be the first result
        var search = await t.Members().SearchPageAsync("12", 0, 10, ct);
        Assert.Equal(12, search.Rows[0].MemberNo);
        Assert.True(search.Total > 1);
    }
}

public class PhotoMaintenanceTests
{
    /// <summary>Pretends every image shrinks to maxPixels bytes, so sizes are predictable.</summary>
    private sealed class FakeImages : IImageProcessor
    {
        public byte[]? ResizeJpeg(byte[] image, int maxPixels, int quality) =>
            image.Length > 0 && image[0] == 0xFF ? [0xFF, .. new byte[Math.Min(image.Length, maxPixels) - 1]] : null;
    }

    [Fact]
    public async Task Optimize_shrinks_large_photos_makes_thumbnails_and_backs_up_first()
    {
        await using var t = await TestDb.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        await using (var db = t.Context())
        {
            db.Members.Add(new Member { MemberNo = 1, FullName = "Big", Photo = new MemberPhoto { Data = [0xFF, .. new byte[5000]] } });
            db.Members.Add(new Member { MemberNo = 2, FullName = "Small", Photo = new MemberPhoto { Data = [0xFF, .. new byte[100]] } });
            db.Members.Add(new Member { MemberNo = 3, FullName = "Broken", Photo = new MemberPhoto { Data = [0x00, 1, 2] } });
            await db.SaveChangesAsync(ct);
        }

        var svc = new PhotoMaintenanceService(t.Factory, new FakeImages(), t.User);
        var report = await svc.OptimizePhotosAsync(ct: ct);

        Assert.Equal(1, report.Resized);     // only the one bigger than 600 "px"
        Assert.Equal(2, report.ThumbnailsCreated);
        Assert.Equal(1, report.Unreadable);  // left untouched, not deleted
        Assert.True(File.Exists(report.BackupPath));
        Assert.True(report.BytesAfter < report.BytesBefore);

        await using var check = t.Context();
        Assert.Equal(PhotoSizes.Photo, (await check.MemberPhotos.SingleAsync(p => p.MemberId == 1, ct)).Data.Length);
        Assert.Equal(3, (await check.MemberPhotos.SingleAsync(p => p.MemberId == 3, ct)).Data.Length);
        File.Delete(report.BackupPath);
    }
}

public class ImportSkipReasonTests
{
    [Fact]
    public async Task Re_import_explains_every_skip()
    {
        await using var t = await TestDb.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        var file = LegacyFixture.CreateWithMember();
        await t.Importer().ImportAsync(file, ct: ct);
        var again = await t.Importer().ImportAsync(file, ct: ct);

        Assert.Equal(again.Skipped, again.SkipReasons.Values.Sum());
        Assert.Equal(1, again.SkipReasons["Member: already imported"]);
        Assert.Contains("Attendance: already imported", again.ToString());
    }
}
