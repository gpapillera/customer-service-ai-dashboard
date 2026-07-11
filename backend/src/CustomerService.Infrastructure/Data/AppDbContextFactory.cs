using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CustomerService.Infrastructure.Data;

/// <summary>
/// Design-time factory so the EF Core CLI can build <see cref="AppDbContext"/>
/// without booting the full web host (needed for `dotnet ef migrations`).
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    /// <inheritdoc/>
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost,1433;Database=CustomerServiceDb;User Id=csadmin;Password=P@ssw0rd_2024_Xq;TrustServerCertificate=True;")
            .Options;
        return new AppDbContext(options);
    }
}
