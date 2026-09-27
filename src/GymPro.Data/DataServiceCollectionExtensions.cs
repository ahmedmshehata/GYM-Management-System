using GymPro.Core.Configuration;
using GymPro.Core.Security;
using GymPro.Data.Import;
using GymPro.Data.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GymPro.Data;

public static class DataServiceCollectionExtensions
{
    public static IServiceCollection AddGymData(this IServiceCollection services, DatabaseOptions db)
    {
        var connectionString = BuildConnectionString(db);
        services.AddDbContextFactory<GymDbContext>(o => o.UseSqlite(connectionString));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<CurrentUser>();
        services.AddSingleton<ICurrentUser>(sp => sp.GetRequiredService<CurrentUser>());
        services.AddTransient<AuthService>();
        services.AddTransient<UserService>();
        services.AddTransient<MemberService>();
        services.AddTransient<PlanService>();
        services.AddTransient<SubscriptionService>();
        services.AddTransient<AuditQueryService>();
        services.AddTransient<LegacyImporter>();
        services.AddTransient<PhotoMaintenanceService>();
        return services;
    }

    public static string ResolvePath(DatabaseOptions db)
    {
        var path = Environment.ExpandEnvironmentVariables(db.Path);
        return Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
    }

    public static string BuildConnectionString(DatabaseOptions db) => new SqliteConnectionStringBuilder
    {
        DataSource = ResolvePath(db),
        ForeignKeys = true,
        Pooling = true,
        DefaultTimeout = db.BusyTimeoutSeconds, // Microsoft.Data.Sqlite retries SQLITE_BUSY until this elapses
    }.ToString();
}
