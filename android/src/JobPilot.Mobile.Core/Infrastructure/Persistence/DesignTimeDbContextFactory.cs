using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JobPilot.Mobile.Core.Infrastructure.Persistence;

/// <summary>Used only by <c>dotnet ef migrations add</c>; the app builds its context from <see cref="MobilePaths"/>.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MobileDbContext>
{
    public MobileDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<MobileDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
