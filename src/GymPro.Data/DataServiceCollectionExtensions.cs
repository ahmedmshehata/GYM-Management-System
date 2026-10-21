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

    /// <summary>
    /// A brand that moves the data folder (Branding:DataFolderName) must not silently start with an empty database
    /// while the members still live in the default folder. Returns both folders when that would happen, else null.
    /// Only applies when Database:Path uses the {DataFolder} placeholder; an explicit path is taken as-is.
    /// </summary>
    public static (string NewFolder, string ExistingFolder)? FindDataLeftBehind(DatabaseOptions configured, string dataFolderName, string defaultFolderName)
    {
        if (!configured.Path.Contains(DatabaseOptions.DataFolderToken, StringComparison.OrdinalIgnoreCase)
            || string.Equals(dataFolderName, defaultFolderName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var target = ResolvePath(new DatabaseOptions { Path = configured.PathFor(dataFolderName) });
        var existing = ResolvePath(new DatabaseOptions { Path = configured.PathFor(defaultFolderName) });
        return File.Exists(target) || !File.Exists(existing)
            ? null
            : (Path.GetDirectoryName(target)!, Path.GetDirectoryName(existing)!);
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
