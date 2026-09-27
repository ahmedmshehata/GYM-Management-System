using GymPro.Core.Entities;
using GymPro.Core.Rules;
using GymPro.Core.Security;

namespace GymPro.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Verifies_correct_password_and_rejects_wrong_one()
    {
        var hash = PasswordHasher.Hash("s3cret-pass");
        Assert.True(PasswordHasher.Verify("s3cret-pass", hash));
        Assert.False(PasswordHasher.Verify("S3cret-pass", hash));
        Assert.False(PasswordHasher.Verify("s3cret-pass", "garbage"));
        Assert.NotEqual(hash, PasswordHasher.Hash("s3cret-pass")); // salted
    }
}

public class MembershipRulesTests
{
    private static readonly DateOnly Today = new(2026, 9, 26);

    private static Subscription Pack(int sessions = 12) => new()
    {
        StartDate = Today.AddDays(-5),
        EndDate = Today.AddDays(10),
        SessionsAllowed = sessions,
    };

    [Fact]
    public void Session_pack_counts_down_and_blocks_when_used_up()
    {
        Assert.Equal(2, MembershipRules.Evaluate(Pack(), 10, false, Today).SessionsLeft);
        Assert.Equal(CheckInBlock.NoSessionsLeft, MembershipRules.Evaluate(Pack(), 12, false, Today).Block);
    }

    [Fact]
    public void Blocks_expired_future_cancelled_and_second_visit_same_day()
    {
        var s = Pack();
        Assert.Equal(CheckInBlock.Expired, MembershipRules.Evaluate(s, 0, false, Today.AddDays(11)).Block);
        Assert.Equal(CheckInBlock.NotStarted, MembershipRules.Evaluate(s, 0, false, Today.AddDays(-6)).Block);
        Assert.Equal(CheckInBlock.AlreadyCheckedInToday, MembershipRules.Evaluate(s, 1, true, Today).Block);
        s.Status = SubscriptionStatus.Cancelled;
        Assert.Equal(CheckInBlock.Cancelled, MembershipRules.Evaluate(s, 0, false, Today).Block);
    }

    [Fact]
    public void Days_left_includes_today_and_monthly_end_date_is_inclusive()
    {
        var plan = new Plan { Name = "Month", DurationMonths = 1 };
        var start = new DateOnly(2026, 1, 15);
        Assert.Equal(new DateOnly(2026, 2, 14), MembershipRules.EndDateFor(plan, start));
        var s = new Subscription { StartDate = Today, EndDate = Today };
        Assert.Equal(1, MembershipRules.Evaluate(s, 0, false, Today).DaysLeft);
    }

    [Fact]
    public void Role_permissions_are_cumulative()
    {
        Assert.True(Permissions.Has(UserRole.Reception, Permission.CheckIn));
        Assert.False(Permissions.Has(UserRole.Reception, Permission.ViewAudit));
        Assert.True(Permissions.Has(UserRole.Manager, Permission.ViewAudit));
        Assert.False(Permissions.Has(UserRole.Manager, Permission.ManageUsers));
        Assert.True(Permissions.Has(UserRole.Admin, Permission.ImportLegacy));
        Assert.False(Permissions.Has(null, Permission.CheckIn));
    }
}
