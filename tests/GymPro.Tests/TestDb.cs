using GymPro.Core.Configuration;
using GymPro.Core.Entities;
using GymPro.Core.Security;
using GymPro.Data;
using GymPro.Data.Import;
using GymPro.Data.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GymPro.Tests;

public sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

/// <summary>A migrated SQLite file in %TEMP% with an admin signed in.</summary>
public sealed class TestDb : IAsyncDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"gympro-test-{Guid.NewGuid():N}.db");

    public CurrentUser User { get; } = new();
    public FixedTime Time { get; } = new(new DateTimeOffset(2026, 9, 26, 10, 0, 0, TimeSpan.Zero));
    public IDbContextFactory<GymDbContext> Factory { get; private set; } = null!;
    public IOptions<SecurityOptions> Security { get; } = Options.Create(new SecurityOptions());

    public static async Task<TestDb> CreateAsync(UserRole role = UserRole.Admin)
    {
        var t = new TestDb();
        var options = new DbContextOptionsBuilder<GymDbContext>()
            .UseSqlite(DataServiceCollectionExtensions.BuildConnectionString(new DatabaseOptions { Path = t._path }))
            .Options;
        t.Factory = new ContextFactory(options, t.User);
        await DbInitializer.InitializeAsync(t.Factory, new DatabaseOptions { Path = t._path });
        t.User.SignIn(new AppUser { Id = 1, UserName = "tester", DisplayName = "Tester", PasswordHash = "x", Role = role });
        return t;
    }

    public GymDbContext Context() => Factory.CreateDbContext();
    public AuthService Auth() => new(Factory, User, Security, Time);
    public MemberService Members() => new(Factory, User, Time);
    public PlanService Plans() => new(Factory, User);
    public SubscriptionService Subscriptions() => new(Factory, User, Time);
    public LegacyImporter Importer() => new(Factory, User);

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            try { File.Delete(f); } catch (IOException) { }
        }

        return ValueTask.CompletedTask;
    }

    private sealed class ContextFactory(DbContextOptions<GymDbContext> options, ICurrentUser user) : IDbContextFactory<GymDbContext>
    {
        public GymDbContext CreateDbContext() => new(options, user);
    }
}
