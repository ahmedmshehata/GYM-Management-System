using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GymPro.Data;

/// <summary>Used only by <c>dotnet ef migrations</c>.</summary>
internal sealed class DesignTimeFactory : IDesignTimeDbContextFactory<GymDbContext>
{
    public GymDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<GymDbContext>().UseSqlite("Data Source=design.db").Options);
}
