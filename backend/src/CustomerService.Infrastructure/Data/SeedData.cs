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
        new Customer { Name = "Juan Dela Cruz", Email = "juan@acme.ph", Phone = "+639171234567", Company = "ACME Retail", Address = "Manila", CreatedAtUtc = DateTime.UtcNow.AddDays(-120) },
        new Customer { Name = "Maria Clara", Email = "maria.c@bcd.com", Phone = "+639189999888", Company = "BCD Corp", Address = "Cebu", CreatedAtUtc = DateTime.UtcNow.AddDays(-90) },
        new Customer { Name = "Pedro Penduko", Email = "pedro@xyz.io", Phone = "+639151111222", Company = "XYZ Logistics", Address = "Davao", CreatedAtUtc = DateTime.UtcNow.AddDays(-60) },
        new Customer { Name = "Ana Reyes", Email = "ana@supplychain.com", Phone = "+639177777333", Company = "SupplyChain Inc", Address = "Quezon City", CreatedAtUtc = DateTime.UtcNow.AddDays(-30) },
        new Customer { Name = "Liza Lopez", Email = "liza@northwind.ph", Phone = "+639161234444", Company = "Northwind Traders", Address = "Makati", CreatedAtUtc = DateTime.UtcNow.AddDays(-28) },
        new Customer { Name = "Carlos Mendoza", Email = "carlos@globex.com", Phone = "+639172345555", Company = "Globex", Address = "Pasig", CreatedAtUtc = DateTime.UtcNow.AddDays(-25) },
        new Customer { Name = "Sofia Reyes", Email = "sofia@initech.ph", Phone = "+639183456666", Company = "Initech", Address = "Taguig", CreatedAtUtc = DateTime.UtcNow.AddDays(-22) },
        new Customer { Name = "Benjie Cruz", Email = "benjie@umbrella.io", Phone = "+639194567777", Company = "Umbrella Corp", Address = "Mandaluyong", CreatedAtUtc = DateTime.UtcNow.AddDays(-18) },
        new Customer { Name = "Grace Tan", Email = "grace@hooli.com", Phone = "+639105678888", Company = "Hooli", Address = "Paranaque", CreatedAtUtc = DateTime.UtcNow.AddDays(-15) },
        new Customer { Name = "Mark Villanueva", Email = "mark@stark.ph", Phone = "+639116789999", Company = "Stark Industries", Address = "Muntinlupa", CreatedAtUtc = DateTime.UtcNow.AddDays(-12) },
        new Customer { Name = "Ella Garcia", Email = "ella@wayne.com", Phone = "+639127890000", Company = "Wayne Enterprises", Address = "Las Pinas", CreatedAtUtc = DateTime.UtcNow.AddDays(-9) },
    };

    /// <summary>Returns the seed cases (depends on customers/categories/users).</summary>
    /// <param name="customers">Seeded customers (navigation links).</param>
    /// <param name="categories">Seeded categories (navigation links).</param>
    /// <returns>List of cases.</returns>
    public static List<Case> Cases(IReadOnlyList<Customer> customers, IReadOnlyList<Category> categories) => new()
    {
        new Case { Subject = "Double charged on invoice", Description = "URGENT: I was billed twice this month, need a refund ASAP.", Customer = customers[0], Category = categories[0], Status = CaseStatus.InProgress, Priority = Priority.High, PriorityAutoSuggested = false, AssignedToUserId = "agent-001", CreatedAtUtc = DateTime.UtcNow.AddDays(-12) },
        new Case { Subject = "Package not delivered", Description = "My order has not arrived in 10 days, tracking is stuck.", Customer = customers[1], Category = categories[1], Status = CaseStatus.New, Priority = Priority.Medium, PriorityAutoSuggested = true, AssignedToUserId = "agent-002", CreatedAtUtc = DateTime.UtcNow.AddDays(-5) },
        new Case { Subject = "API returning 500 errors", Description = "Critical outage: our integration is down, please escalate immediately.", Customer = customers[2], Category = categories[2], Status = CaseStatus.InProgress, Priority = Priority.High, PriorityAutoSuggested = false, AssignedToUserId = "agent-001", CreatedAtUtc = DateTime.UtcNow.AddDays(-2) },
        new Case { Subject = "Cannot reset password", Description = "Reset link is not arriving in my inbox.", Customer = customers[3], Category = categories[3], Status = CaseStatus.Closed, Priority = Priority.Low, PriorityAutoSuggested = false, AssignedToUserId = "agent-002", CreatedAtUtc = DateTime.UtcNow.AddDays(-20) },
        new Case { Subject = "Request warranty replacement", Description = "Product arrived broken, would like a replacement.", Customer = customers[0], Category = categories[4], Status = CaseStatus.Resolved, Priority = Priority.Medium, PriorityAutoSuggested = true, AssignedToUserId = "agent-001", CreatedAtUtc = DateTime.UtcNow.AddDays(-1) },
        new Case { Subject = "Integration webhook failing", Description = "Our webhook endpoint returns 402, orders are not syncing.", Customer = customers[4], Category = categories[2], Status = CaseStatus.InProgress, Priority = Priority.High, PriorityAutoSuggested = false, AssignedToUserId = "agent-001", CreatedAtUtc = DateTime.UtcNow.AddDays(-3) },
        new Case { Subject = "Wrong amount on receipt", Description = "Receipt shows a higher amount than the quote.", Customer = customers[5], Category = categories[0], Status = CaseStatus.New, Priority = Priority.Medium, PriorityAutoSuggested = true, AssignedToUserId = "agent-002", CreatedAtUtc = DateTime.UtcNow.AddDays(-4) },
        new Case { Subject = "Item arrived damaged", Description = "Box was crushed in transit, item bent.", Customer = customers[6], Category = categories[1], Status = CaseStatus.Escalated, Priority = Priority.High, PriorityAutoSuggested = false, AssignedToUserId = "agent-001", CreatedAtUtc = DateTime.UtcNow.AddDays(-6) },
        new Case { Subject = "Cannot enable 2FA", Description = "Authenticator app keeps rejecting the code.", Customer = customers[7], Category = categories[3], Status = CaseStatus.InProgress, Priority = Priority.Low, PriorityAutoSuggested = false, AssignedToUserId = "agent-002", CreatedAtUtc = DateTime.UtcNow.AddDays(-7) },
        new Case { Subject = "Feature request: bulk export", Description = "Would like to export all cases to CSV.", Customer = customers[8], Category = categories[4], Status = CaseStatus.New, Priority = Priority.Medium, PriorityAutoSuggested = true, AssignedToUserId = "agent-001", CreatedAtUtc = DateTime.UtcNow.AddDays(-8) },
        new Case { Subject = "Dashboard latency spike", Description = "Loading times tripled after the last deploy.", Customer = customers[9], Category = categories[2], Status = CaseStatus.Resolved, Priority = Priority.High, PriorityAutoSuggested = false, AssignedToUserId = "agent-002", CreatedAtUtc = DateTime.UtcNow.AddDays(-9) },
        new Case { Subject = "Duplicate invoice dispute", Description = "Finance flagged a possible duplicate billing.", Customer = customers[10], Category = categories[0], Status = CaseStatus.Closed, Priority = Priority.Low, PriorityAutoSuggested = false, AssignedToUserId = "agent-001", CreatedAtUtc = DateTime.UtcNow.AddDays(-15) },
        new Case { Subject = "Login blocked after password change", Description = "Account locked right after I updated my password.", Customer = customers[4], Category = categories[3], Status = CaseStatus.New, Priority = Priority.Medium, PriorityAutoSuggested = true, AssignedToUserId = "agent-002", CreatedAtUtc = DateTime.UtcNow.AddDays(-1) },
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
