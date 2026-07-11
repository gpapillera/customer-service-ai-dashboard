using CustomerService.Domain.Entities;

namespace CustomerService.Infrastructure.Data;

/// <summary>
/// Provides deterministic seed data so the app is usable on first run.
/// Passwords are hashed with BCrypt; the demo credentials are documented in
/// the README.
/// </summary>
public static class SeedData
{
    /// <summary>BCrypt-hashed password shared by all seed users ("Passw0rd!").</summary>
    public const string DemoPasswordHash = "$2a$11$8G8k1hZ1hZ1hZ1hZ1hZ1hO8G8k1hZ1hZ1hZ1hZ1hZ1hZ1hZ1hZ1hZ"; // placeholder; replaced at runtime

    /// <summary>Returns the seed categories.</summary>
    /// <returns>List of categories.</returns>
    public static List<Category> Categories() => new()
    {
        new Category { Name = "Billing", Description = "Invoices, payments, refunds" },
        new Category { Name = "Shipping", Description = "Delivery, tracking, logistics" },
        new Category { Name = "Technical", Description = "Bugs, outages, integration" },
        new Category { Name = "Account", Description = "Login, profile, access" },
        new Category { Name = "Product", Description = "Features, returns, warranty" },
    };

    /// <summary>Returns the seed users (admin + agents).</summary>
    /// <returns>List of users.</returns>
    public static List<User> Users() => new()
    {
        new User { Id = "admin-001", UserName = "admin", FullName = "Ada Admin", Email = "admin@demo.com", Role = UserRole.Admin },
        new User { Id = "agent-001", UserName = "agent", FullName = "Grace Agent", Email = "agent@demo.com", Role = UserRole.Agent },
        new User { Id = "agent-002", UserName = "maria", FullName = "Maria Santos", Email = "maria@demo.com", Role = UserRole.Agent },
    };

    /// <summary>Returns the seed customers.</summary>
    /// <returns>List of customers.</returns>
    public static List<Customer> Customers() => new()
    {
        new Customer { Name = "Juan Dela Cruz", Email = "juan@acme.ph", Phone = "+639171234567", Company = "ACME Retail", Address = "Manila" },
        new Customer { Name = "Maria Clara", Email = "maria.c@bcd.com", Phone = "+639189999888", Company = "BCD Corp", Address = "Cebu" },
        new Customer { Name = "Pedro Penduko", Email = "pedro@xyz.io", Phone = "+639151111222", Company = "XYZ Logistics", Address = "Davao" },
        new Customer { Name = "Ana Reyes", Email = "ana@supplychain.com", Phone = "+639177777333", Company = "SupplyChain Inc", Address = "Quezon City" },
    };

    /// <summary>Returns the seed cases (depends on customers/categories/users).</summary>
    /// <param name="customers">Seeded customers (navigation links).</param>
    /// <param name="categories">Seeded categories (navigation links).</param>
    /// <returns>List of cases.</returns>
    public static List<Case> Cases(IReadOnlyList<Customer> customers, IReadOnlyList<Category> categories) => new()
    {
        new Case { Subject = "Double charged on invoice", Description = "URGENT: I was billed twice this month, need a refund ASAP.", Customer = customers[0], Category = categories[0], Status = CaseStatus.InProgress, Priority = Priority.High, PriorityAutoSuggested = false, AssignedToUserId = "agent-001", CreatedAtUtc = DateTime.UtcNow.AddDays(-12) },
        new Case { Subject = "Package not delivered", Description = "My order has not arrived in 10 days, tracking is stuck.", Customer = customers[1], Category = categories[1], Status = CaseStatus.New, Priority = Priority.Medium, AssignedToUserId = "agent-002", CreatedAtUtc = DateTime.UtcNow.AddDays(-5) },
        new Case { Subject = "API returning 500 errors", Description = "Critical outage: our integration is down, please escalate immediately.", Customer = customers[2], Category = categories[2], Status = CaseStatus.InProgress, Priority = Priority.High, AssignedToUserId = "agent-001", CreatedAtUtc = DateTime.UtcNow.AddDays(-2) },
        new Case { Subject = "Cannot reset password", Description = "Reset link is not arriving in my inbox.", Customer = customers[3], Category = categories[3], Status = CaseStatus.Closed, Priority = Priority.Low, AssignedToUserId = "agent-002", CreatedAtUtc = DateTime.UtcNow.AddDays(-20) },
        new Case { Subject = "Request warranty replacement", Description = "Product arrived broken, would like a replacement.", Customer = customers[0], Category = categories[4], Status = CaseStatus.OnHold, Priority = Priority.Medium, AssignedToUserId = "agent-001", CreatedAtUtc = DateTime.UtcNow.AddDays(-1) },
    };

    /// <summary>Returns the seed call logs (depends on cases).</summary>
    /// <param name="cases">The seeded cases, used to wire navigation links.</param>
    /// <returns>List of call logs.</returns>
    public static List<CallLog> CallLogs(IReadOnlyList<Case> cases) => new()
    {
        new CallLog { Case = cases[0], Direction = CallDirection.Inbound, Notes = "Customer called, very angry about double charge.", DurationSeconds = 240, LoggedByUserId = "agent-001", CreatedAtUtc = DateTime.UtcNow.AddDays(-11) },
        new CallLog { Case = cases[0], Direction = CallDirection.Outbound, Notes = "Confirmed refund processed, closed loop.", DurationSeconds = 180, LoggedByUserId = "agent-001", CreatedAtUtc = DateTime.UtcNow.AddDays(-10) },
        new CallLog { Case = cases[2], Direction = CallDirection.Outbound, Notes = "Escalated to engineering, ETA 4h.", DurationSeconds = 300, LoggedByUserId = "agent-001", CreatedAtUtc = DateTime.UtcNow.AddDays(-2) },
    };
}
