using CustomerService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustomerService.Infrastructure.Data;

/// <summary>
/// Idempotent seeder: inserts demo categories, users, customers, cases and
/// call logs only when the database is empty. User passwords are hashed with
/// BCrypt at seed time.
/// See docs/DIY.md §2 for the SQLite fallback and idempotent-seed walkthrough.
/// </summary>
public static class SeedDataInitializer
{
    /// <summary>Demo password shared by all seed users.</summary>
    public const string DemoPassword = "Passw0rd!";

    /// <summary>Populates the database if it has no categories yet.</summary>
    /// <param name="ctx">The database context.</param>
    public static void Initialize(AppDbContext ctx)
    {
        if (ctx.Categories.Any()) return;

        var categories = SeedData.Categories();
        ctx.Categories.AddRange(categories);
        foreach (var u in SeedData.Users())
        {
            u.PasswordHash = BCrypt.Net.BCrypt.HashPassword(DemoPassword);
            ctx.Users.Add(u);
        }
        var customers = SeedData.Customers();
        ctx.Customers.AddRange(customers);
        var cases = SeedData.Cases(customers, categories);
        ctx.Cases.AddRange(cases);
        ctx.CallLogs.AddRange(SeedData.CallLogs(cases));
        ctx.SaveChanges();
    }
}
