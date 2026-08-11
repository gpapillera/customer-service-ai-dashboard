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
        new User { Id = "admin-001", UserName = "admin", FullName = "Ada Admin", Email = "admin@demo.com", Role = UserRole.Admin, AgentDisplayId = "ADM-001", ProfilePictureUrl = "https://api.dicebear.com/7.x/avataaars/svg?seed=admin" },
        new User { Id = "agent-001", UserName = "agent", FullName = "Grace Agent", Email = "agent@demo.com", Role = UserRole.Agent, AgentDisplayId = "AGT-001", ProfilePictureUrl = "https://api.dicebear.com/7.x/avataaars/svg?seed=agent" },
        new User { Id = "agent-002", UserName = "maria", FullName = "Maria Santos", Email = "maria@demo.com", Role = UserRole.Agent, AgentDisplayId = "AGT-002", ProfilePictureUrl = "https://api.dicebear.com/7.x/avataaars/svg?seed=maria" },
    };

    /// <summary>Returns the seed customers.</summary>
    /// <returns>List of customers.</returns>
    public static List<Customer> Customers() => new()
    {
        new Customer { CustomerDisplayId = "C-00001", Name = "Juan Dela Cruz", Email = "juan@acme.ph", Phone = "+639171234567", Company = "ACME Retail", Address = "Manila", CreatedAtUtc = DateTime.UtcNow.AddDays(-120) },
        new Customer { CustomerDisplayId = "C-00002", Name = "Maria Clara", Email = "maria.c@bcd.com", Phone = "+639189999888", Company = "BCD Corp", Address = "Cebu", CreatedAtUtc = DateTime.UtcNow.AddDays(-90) },
        new Customer { CustomerDisplayId = "C-00003", Name = "Pedro Penduko", Email = "pedro@xyz.io", Phone = "+639151111222", Company = "XYZ Logistics", Address = "Davao", CreatedAtUtc = DateTime.UtcNow.AddDays(-60) },
        new Customer { CustomerDisplayId = "C-00004", Name = "Ana Reyes", Email = "ana@supplychain.com", Phone = "+639177777333", Company = "SupplyChain Inc", Address = "Quezon City", CreatedAtUtc = DateTime.UtcNow.AddDays(-30) },
        new Customer { CustomerDisplayId = "C-00005", Name = "Liza Lopez", Email = "liza@northwind.ph", Phone = "+639161234444", Company = "Northwind Traders", Address = "Makati", CreatedAtUtc = DateTime.UtcNow.AddDays(-28) },
        new Customer { CustomerDisplayId = "C-00006", Name = "Carlos Mendoza", Email = "carlos@globex.com", Phone = "+639172345555", Company = "Globex", Address = "Pasig", CreatedAtUtc = DateTime.UtcNow.AddDays(-25) },
        new Customer { CustomerDisplayId = "C-00007", Name = "Sofia Reyes", Email = "sofia@initech.ph", Phone = "+639183456666", Company = "Initech", Address = "Taguig", CreatedAtUtc = DateTime.UtcNow.AddDays(-22) },
        new Customer { CustomerDisplayId = "C-00008", Name = "Benjie Cruz", Email = "benjie@umbrella.io", Phone = "+639194567777", Company = "Umbrella Corp", Address = "Mandaluyong", CreatedAtUtc = DateTime.UtcNow.AddDays(-18) },
        new Customer { CustomerDisplayId = "C-00009", Name = "Grace Tan", Email = "grace@hooli.com", Phone = "+639105678888", Company = "Hooli", Address = "Paranaque", CreatedAtUtc = DateTime.UtcNow.AddDays(-15) },
        new Customer { CustomerDisplayId = "C-00010", Name = "Mark Villanueva", Email = "mark@stark.ph", Phone = "+639116789999", Company = "Stark Industries", Address = "Muntinlupa", CreatedAtUtc = DateTime.UtcNow.AddDays(-12) },
        new Customer { CustomerDisplayId = "C-00011", Name = "Ella Garcia", Email = "ella@wayne.com", Phone = "+639127890000", Company = "Wayne Enterprises", Address = "Las Pinas", CreatedAtUtc = DateTime.UtcNow.AddDays(-9) },
    };

    /// <summary>Returns the seed cases (depends on customers/categories/users).</summary>
    /// <param name="customers">Seeded customers (navigation links).</param>
    /// <param name="categories">Seeded categories (navigation links).</param>
    /// <returns>List of cases.</returns>
    public static List<Case> Cases(IReadOnlyList<Customer> customers, IReadOnlyList<Category> categories) => new()
    {
        new Case { CaseDisplayId = "CAS-00001", Subject = "Double charged on invoice", Description = "URGENT: I was billed twice this month, need a refund ASAP.", Customer = customers[0], Category = categories[0], Status = CaseStatus.InProgress, Priority = Priority.High, PriorityAutoSuggested = false, AssignedToUserId = "agent-001", CreatedAtUtc = DateTime.UtcNow.AddDays(-12) },
        new Case { CaseDisplayId = "CAS-00002", Subject = "Package not delivered", Description = "My order has not arrived in 10 days, tracking is stuck.", Customer = customers[1], Category = categories[1], Status = CaseStatus.New, Priority = Priority.Medium, PriorityAutoSuggested = true, AssignedToUserId = "agent-002", CreatedAtUtc = DateTime.UtcNow.AddDays(-5), FollowUpDueUtc = DateTime.UtcNow.AddDays(-1) },
        new Case { CaseDisplayId = "CAS-00003", Subject = "API returning 500 errors", Description = "Critical outage: our integration is down, please escalate immediately.", Customer = customers[2], Category = categories[2], Status = CaseStatus.InProgress, Priority = Priority.High, PriorityAutoSuggested = false, AssignedToUserId = "agent-001", CreatedAtUtc = DateTime.UtcNow.AddDays(-2) },
        new Case { CaseDisplayId = "CAS-00004", Subject = "Cannot reset password", Description = "Reset link is not arriving in my inbox.", Customer = customers[3], Category = categories[3], Status = CaseStatus.Closed, Priority = Priority.Low, PriorityAutoSuggested = false, AssignedToUserId = "agent-002", CreatedAtUtc = DateTime.UtcNow.AddDays(-20) },
        new Case { CaseDisplayId = "CAS-00005", Subject = "Request warranty replacement", Description = "Product arrived broken, would like a replacement.", Customer = customers[0], Category = categories[4], Status = CaseStatus.Resolved, Priority = Priority.Medium, PriorityAutoSuggested = true, AssignedToUserId = "agent-001", CreatedAtUtc = DateTime.UtcNow.AddDays(-1) },
        new Case { CaseDisplayId = "CAS-00006", Subject = "Integration webhook failing", Description = "Our webhook endpoint returns 402, orders are not syncing.", Customer = customers[4], Category = categories[2], Status = CaseStatus.InProgress, Priority = Priority.High, PriorityAutoSuggested = false, AssignedToUserId = "agent-001", CreatedAtUtc = DateTime.UtcNow.AddDays(-3), FollowUpDueUtc = DateTime.UtcNow.AddDays(-2) },
        new Case { CaseDisplayId = "CAS-00007", Subject = "Wrong amount on receipt", Description = "Receipt shows a higher amount than the quote.", Customer = customers[5], Category = categories[0], Status = CaseStatus.New, Priority = Priority.Medium, PriorityAutoSuggested = true, AssignedToUserId = "agent-002", CreatedAtUtc = DateTime.UtcNow.AddDays(-4) },
        new Case { CaseDisplayId = "CAS-00008", Subject = "Item arrived damaged", Description = "Box was crushed in transit, item bent.", Customer = customers[6], Category = categories[1], Status = CaseStatus.Escalated, Priority = Priority.High, PriorityAutoSuggested = false, AssignedToUserId = "agent-001", CreatedAtUtc = DateTime.UtcNow.AddDays(-6) },
        new Case { CaseDisplayId = "CAS-00009", Subject = "Cannot enable 2FA", Description = "Authenticator app keeps rejecting the code.", Customer = customers[7], Category = categories[3], Status = CaseStatus.InProgress, Priority = Priority.Low, PriorityAutoSuggested = false, AssignedToUserId = "agent-002", CreatedAtUtc = DateTime.UtcNow.AddDays(-7) },
        new Case { CaseDisplayId = "CAS-00010", Subject = "Feature request: bulk export", Description = "Would like to export all cases to CSV.", Customer = customers[8], Category = categories[4], Status = CaseStatus.New, Priority = Priority.Medium, PriorityAutoSuggested = true, AssignedToUserId = "agent-001", CreatedAtUtc = DateTime.UtcNow.AddDays(-8) },
        new Case { CaseDisplayId = "CAS-00011", Subject = "Dashboard latency spike", Description = "Loading times tripled after the last deploy.", Customer = customers[9], Category = categories[2], Status = CaseStatus.Resolved, Priority = Priority.High, PriorityAutoSuggested = false, AssignedToUserId = "agent-002", CreatedAtUtc = DateTime.UtcNow.AddDays(-9) },
        new Case { CaseDisplayId = "CAS-00012", Subject = "Duplicate invoice dispute", Description = "Finance flagged a possible duplicate billing.", Customer = customers[10], Category = categories[0], Status = CaseStatus.Closed, Priority = Priority.Low, PriorityAutoSuggested = false, AssignedToUserId = "agent-001", CreatedAtUtc = DateTime.UtcNow.AddDays(-15) },
        new Case { CaseDisplayId = "CAS-00013", Subject = "Login blocked after password change", Description = "Account locked right after I updated my password.", Customer = customers[4], Category = categories[3], Status = CaseStatus.New, Priority = Priority.Medium, PriorityAutoSuggested = true, AssignedToUserId = "agent-002", CreatedAtUtc = DateTime.UtcNow.AddDays(-1) },
        // --- Additional cases to bring total to 21 ---
        new Case { CaseDisplayId = "CAS-00014", Subject = "Subscription auto-renew dispute", Description = "Charged for renewal despite cancelling last month. Requesting reversal.", Customer = customers[6], Category = categories[0], Status = CaseStatus.InProgress, Priority = Priority.Medium, PriorityAutoSuggested = true, AssignedToUserId = "agent-001", CreatedAtUtc = DateTime.UtcNow.AddDays(-10) },
        new Case { CaseDisplayId = "CAS-00015", Subject = "International shipping delay", Description = "Package has been stuck in customs for 2 weeks. Need update.", Customer = customers[9], Category = categories[1], Status = CaseStatus.New, Priority = Priority.Low, PriorityAutoSuggested = false, AssignedToUserId = "agent-002", CreatedAtUtc = DateTime.UtcNow.AddDays(-7) },
        new Case { CaseDisplayId = "CAS-00016", Subject = "Mobile app crash on login", Description = "App crashes immediately after entering credentials. iOS 18.", Customer = customers[2], Category = categories[2], Status = CaseStatus.Escalated, Priority = Priority.High, PriorityAutoSuggested = true, AssignedToUserId = "agent-001", CreatedAtUtc = DateTime.UtcNow.AddDays(-11) },
        new Case { CaseDisplayId = "CAS-00017", Subject = "Unable to update profile photo", Description = "Getting a validation error when uploading a new profile image.", Customer = customers[5], Category = categories[3], Status = CaseStatus.Resolved, Priority = Priority.Low, PriorityAutoSuggested = false, AssignedToUserId = "agent-002", CreatedAtUtc = DateTime.UtcNow.AddDays(-14) },
        new Case { CaseDisplayId = "CAS-00018", Subject = "Refund for defective unit", Description = "Received a DOA unit. Serial #SN-98765. Requesting full refund.", Customer = customers[3], Category = categories[4], Status = CaseStatus.New, Priority = Priority.Medium, PriorityAutoSuggested = true, AssignedToUserId = "agent-001", CreatedAtUtc = DateTime.UtcNow.AddDays(-3) },
        new Case { CaseDisplayId = "CAS-00019", Subject = "Invoice #INV-3344 missing line items", Description = "The PDF invoice doesn't show the discount that was applied.", Customer = customers[7], Category = categories[0], Status = CaseStatus.InProgress, Priority = Priority.Medium, PriorityAutoSuggested = false, AssignedToUserId = "agent-002", CreatedAtUtc = DateTime.UtcNow.AddDays(-6) },
        new Case { CaseDisplayId = "CAS-00020", Subject = "SSL certificate expiring soon", Description = "Warning: our integration certificate expires in 7 days.", Customer = customers[1], Category = categories[2], Status = CaseStatus.New, Priority = Priority.High, PriorityAutoSuggested = true, AssignedToUserId = "agent-001", CreatedAtUtc = DateTime.UtcNow.AddDays(-1), FollowUpDueUtc = DateTime.UtcNow.AddDays(5) },
        new Case { CaseDisplayId = "CAS-00021", Subject = "Bulk discount not applied", Description = "Order of 50 units should have qualified for tier-2 pricing.", Customer = customers[10], Category = categories[4], Status = CaseStatus.New, Priority = Priority.Low, PriorityAutoSuggested = false, AssignedToUserId = "agent-002", CreatedAtUtc = DateTime.UtcNow.AddDays(-2) },
    };

    /// <summary>Returns the seed case comments (depends on cases and the customer/user lists).
    /// Comments create conversation threads so the Messages/Conversations pages show data.</summary>
    /// <param name="cases">The seeded cases.</param>
    /// <returns>List of case comments.</returns>
    public static List<CaseComment> Comments(IReadOnlyList<Case> cases) => new()
    {
        // Case 0 (Double charged) — customer reply + agent response
        new CaseComment { Case = cases[0], Body = "I checked my bank statement and yes, two charges of $49.99 each.", AuthorCustomerId = 1, CreatedAtUtc = DateTime.UtcNow.AddDays(-11).AddHours(2) },
        new CaseComment { Case = cases[0], Body = "Thank you Juan, I can see both transactions. I'm processing a refund for the duplicate now.", AuthorUserId = "agent-001", CreatedAtUtc = DateTime.UtcNow.AddDays(-11).AddHours(4) },
        new CaseComment { Case = cases[0], Body = "Refund has been issued. Please allow 3-5 business days for it to appear.", AuthorUserId = "agent-001", CreatedAtUtc = DateTime.UtcNow.AddDays(-10) },

        // Case 1 (Package not delivered) — customer inquiry
        new CaseComment { Case = cases[1], Body = "Any update on my package? Tracking hasn't moved in days.", AuthorCustomerId = 2, CreatedAtUtc = DateTime.UtcNow.AddDays(-4) },

        // Case 2 (API 500 errors) — technical discussion
        new CaseComment { Case = cases[2], Body = "I've escalated to the engineering team. They're investigating the root cause.", AuthorUserId = "agent-001", CreatedAtUtc = DateTime.UtcNow.AddDays(-2).AddHours(1) },
        new CaseComment { Case = cases[2], Body = "Engineering found the issue — a misconfigured load balancer. Fix is being deployed.", AuthorUserId = "agent-001", CreatedAtUtc = DateTime.UtcNow.AddDays(-2).AddHours(5) },

        // Case 3 (Cannot reset password) — resolved
        new CaseComment { Case = cases[3], Body = "Check your spam folder, the reset link might have been filtered.", AuthorUserId = "agent-002", CreatedAtUtc = DateTime.UtcNow.AddDays(-19) },
        new CaseComment { Case = cases[3], Body = "Found it in spam, thanks! Password reset successfully.", AuthorCustomerId = 4, CreatedAtUtc = DateTime.UtcNow.AddDays(-19).AddHours(3) },

        // Case 5 (Integration webhook failing) — active conversation
        new CaseComment { Case = cases[5], Body = "The 402 error means your endpoint is rejecting our payload. Can you check your server logs?", AuthorUserId = "agent-001", CreatedAtUtc = DateTime.UtcNow.AddDays(-3).AddHours(2) },
        new CaseComment { Case = cases[5], Body = "I see the issue — our webhook URL was updated last week. Let me send the new endpoint.", AuthorCustomerId = 5, CreatedAtUtc = DateTime.UtcNow.AddDays(-3).AddHours(6) },
        new CaseComment { Case = cases[5], Body = "Updated the webhook URL on our end. Please test again.", AuthorUserId = "agent-001", CreatedAtUtc = DateTime.UtcNow.AddDays(-3).AddHours(7) },

        // Case 7 (Item arrived damaged) — escalated
        new CaseComment { Case = cases[7], Body = "Photos of the damaged item attached. The box was clearly crushed.", AuthorCustomerId = 7, CreatedAtUtc = DateTime.UtcNow.AddDays(-5) },
        new CaseComment { Case = cases[7], Body = "I've filed a claim with the shipping carrier and initiated a replacement order.", AuthorUserId = "agent-001", CreatedAtUtc = DateTime.UtcNow.AddDays(-5).AddHours(6) },

        // Case 13 (Subscription auto-renew dispute) — billing conversation
        new CaseComment { Case = cases[13], Body = "I cancelled on the 15th, but the charge went through on the 1st. Something is wrong.", AuthorCustomerId = 7, CreatedAtUtc = DateTime.UtcNow.AddDays(-9) },
        new CaseComment { Case = cases[13], Body = "I see the cancellation request was logged but wasn't processed before the billing cycle. I'll refund the charge and ensure the cancellation goes through.", AuthorUserId = "agent-001", CreatedAtUtc = DateTime.UtcNow.AddDays(-9).AddHours(3) },

        // Case 15 (Mobile app crash) — customer reported
        new CaseComment { Case = cases[15], Body = "This is affecting multiple iOS 18 users. Engineering is aware and working on a hotfix.", AuthorUserId = "agent-001", CreatedAtUtc = DateTime.UtcNow.AddDays(-10) },

        // Case 18 (SSL certificate expiring) — urgent technical
        new CaseComment { Case = cases[18], Body = "We need this renewed ASAP to avoid a production outage.", AuthorUserId = "agent-001", CreatedAtUtc = DateTime.UtcNow.AddDays(-1).AddHours(1) },
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

    /// <summary>Returns the singleton email config seed (test inbox).</summary>
    public static EmailConfig EmailConfig() => new()
    {
        Id = 1,
        TestEmailAddress = "glnppllr@gmail.com",
    };

    /// <summary>Returns the seed allowed email domains (well-known consumer domains).</summary>
    public static List<EmailDomain> EmailDomains() => new()
    {
        new EmailDomain { Domain = "gmail.com", Description = "Google Mail" },
        new EmailDomain { Domain = "yahoo.com", Description = "Yahoo Mail" },
        new EmailDomain { Domain = "outlook.com", Description = "Microsoft Outlook" },
        new EmailDomain { Domain = "hotmail.com", Description = "Microsoft Hotmail" },
        new EmailDomain { Domain = "icloud.com", Description = "Apple iCloud" },
        new EmailDomain { Domain = "proton.me", Description = "Proton Mail" },
        new EmailDomain { Domain = "aol.com", Description = "AOL" },
        new EmailDomain { Domain = "live.com", Description = "Microsoft Live" },
    };

    /// <summary>
    /// Returns the seed per-type email templates. Text is ported from the
    /// previously hard-coded <c>EmailNotificationSender.BuildContent</c> into
    /// editable, token-based templates. Tokens: {{customerName}},
    /// {{customerEmail}}, {{caseId}}, {{caseSubject}}, {{caseStatus}},
    /// {{agentName}}, {{agentEmail}}, {{portalLink}}, {{actionLink}}.
    /// {{portalLink}} = portal homepage for case/overdue/resolved emails, but the
    /// per-recipient activation/reset deep link for CustomerInvite /
    /// CustomerPasswordReset / StaffPasswordReset (see EmailNotificationSender).
    /// {{actionLink}} = the per-recipient activation/reset deep link for ALL
    /// types; prefer it over {{portalLink}} in account-invite/reset templates.
    /// </summary>
    public static List<EmailTemplate> EmailTemplates() => new()
    {
        new EmailTemplate
        {
            Type = "CaseOverdue",
            Subject = "Case #{{caseId}} is overdue: {{caseSubject}}",
            Body = "Hello {{agentName}},\n\n" +
                   "A follow-up on case #{{caseId}} is overdue.\n\n" +
                   "{{caseSubject}}\n\n" +
                   "Please review and follow up at your earliest convenience.\n\n" +
                   "— Customer Service Dashboard",
        },
        new EmailTemplate
        {
            Type = "CaseResolved",
            Subject = "Your case has been {{caseStatus}}: {{caseSubject}}",
            Body = "Hello {{customerName}},\n\n" +
                   "Your support case #{{caseId}} \"{{caseSubject}}\" has been marked {{caseStatus}}.\n\n" +
                   "If you have any further questions, simply reply to this email or open a new request and we'll be happy to help.\n\n" +
                   "Thank you for contacting us,\nCustomer Service Team",
        },
        new EmailTemplate
        {
            Type = "CustomerInvite",
            Subject = "You've been invited to the Customer Portal",
            Body = "Hello {{customerName}},\n\n" +
                   "You've been invited to the Customer Portal.\n\n" +
                   "Click the link below to choose a password and activate your account:\n\n" +
                   "{{actionLink}}\n\n" +
                   "This link expires in 48 hours.\n\n" +
                   "If you weren't expecting this invitation, you can safely ignore this email.\n\n" +
                   "Thank you,\nCustomer Service Team",
        },
        new EmailTemplate
        {
            Type = "CustomerPasswordReset",
            Subject = "Password Reset — Customer Portal",
            Body = "Hello {{customerName}},\n\n" +
                   "We received a request to reset your Customer Portal password.\n\n" +
                   "Click the link below to choose a new password:\n\n" +
                   "{{actionLink}}\n\n" +
                   "This link expires in 48 hours.\n\n" +
                   "If you didn't request a password reset, you can safely ignore this email.\n\n" +
                   "Thank you,\nCustomer Service Team",
        },
        new EmailTemplate
        {
            Type = "StaffPasswordReset",
            Subject = "Password Reset — Staff Account",
            Body = "Hello {{agentName}},\n\n" +
                   "We received a request to reset your staff account password.\n\n" +
                   "Click the link below to set a new password:\n\n" +
                   "{{actionLink}}\n\n" +
                   "This link expires in 48 hours.\n\n" +
                   "If you didn't request a password reset, you can safely ignore this email.\n\n" +
                   "Thank you,\nCustomer Service Dashboard",
        },
        new EmailTemplate
        {
            Type = "NewCustomerMessage",
            Subject = "New customer message on case #{{caseId}}: {{caseSubject}}",
            Body = "Hello {{agentName}},\n\n" +
                   "{{customerName}} posted a new message on case #{{caseId}} \"{{caseSubject}}\".\n\n" +
                   "Please review and respond at your earliest convenience.\n\n" +
                   "— Customer Service Dashboard",
        },
        new EmailTemplate
        {
            Type = "AdminManual",
            Subject = "{{caseSubject}}",
            Body = "Hello,\n\n" +
                   "{{caseSubject}}\n\n" +
                   "— Customer Service Dashboard",
        },
    };
}
