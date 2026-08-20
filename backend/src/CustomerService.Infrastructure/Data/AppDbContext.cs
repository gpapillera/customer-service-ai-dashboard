using CustomerService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustomerService.Infrastructure.Data;

/// <summary>
/// EF Core database context for the Customer Service AI Dashboard.
/// Configures entity mappings and serves as the unit-of-work root.
/// </summary>
public class AppDbContext : DbContext
{
    /// <summary>Initializes a new <see cref="AppDbContext"/>.</summary>
    /// <param name="options">EF Core options.</param>
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    /// <summary>Users (agents/admins).</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>Customers.</summary>
    public DbSet<Customer> Customers => Set<Customer>();

    /// <summary>Customer login accounts (invite + password state).</summary>
    public DbSet<CustomerAccount> CustomerAccounts => Set<CustomerAccount>();

    /// <summary>Case categories.</summary>
    public DbSet<Category> Categories => Set<Category>();

    /// <summary>Cases.</summary>
    public DbSet<Case> Cases => Set<Case>();

    /// <summary>Call / follow-up logs.</summary>
    public DbSet<CallLog> CallLogs => Set<CallLog>();

    /// <summary>Case comments (shared thread between customer + staff).</summary>
    public DbSet<CaseComment> CaseComments => Set<CaseComment>();

    /// <summary>System notifications (e.g. overdue follow-up alerts).</summary>
    public DbSet<Notification> Notifications => Set<Notification>();

    /// <summary>Explicit customer-account activity audit rows (profile edits, etc.).</summary>
    public DbSet<CustomerActivity> CustomerActivities => Set<CustomerActivity>();

    /// <summary>Case/Customer "viewed/opened" audit rows (read events, cooldown-coalesced).</summary>
    public DbSet<ViewEvent> ViewEvents => Set<ViewEvent>();

    /// <summary>Per-agent, per-case "last viewed" markers for the Messages tab.</summary>
    public DbSet<ConversationReadState> ConversationReadStates => Set<ConversationReadState>();

    /// <summary>Singleton email-sending configuration (test address, etc.).</summary>
    public DbSet<EmailConfig> EmailConfigs => Set<EmailConfig>();

    /// <summary>Allowed email domains for direct (non-redirected) delivery.</summary>
    public DbSet<EmailDomain> EmailDomains => Set<EmailDomain>();

    /// <summary>Refresh tokens for the cookie-based auth + rotation flow.</summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>Editable, per-type email templates with personalization tokens.</summary>
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();

    /// <summary>
    /// Configures the model: relationships, constraints, and value
    /// normalization (e.g. lowercase email) at the database level.
    /// </summary>
    /// <param name="builder">Model builder.</param>
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.UserName).IsUnique();
            e.Property(u => u.UserName).IsRequired().HasMaxLength(100);
            e.Property(u => u.PasswordHash).IsRequired();
        });

        builder.Entity<Customer>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).IsRequired().HasMaxLength(200);
            e.Property(c => c.Email).IsRequired().HasMaxLength(200);
            e.Property(c => c.CustomerDisplayId).HasMaxLength(20);
            e.HasMany(c => c.Cases).WithOne(c => c.Customer!)
                .HasForeignKey(c => c.CustomerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(c => c.Account).WithOne(a => a.Customer!)
                .HasForeignKey<CustomerAccount>(a => a.CustomerId);
            e.HasQueryFilter(c => !c.IsDeleted);
        });

        builder.Entity<CustomerAccount>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).ValueGeneratedOnAdd();
            e.HasIndex(a => a.CustomerId).IsUnique();
            e.HasIndex(a => a.InviteToken).IsUnique();
            e.Property(a => a.InviteToken).HasMaxLength(128);
            e.Property(a => a.PasswordHash).HasMaxLength(200);
        });

        builder.Entity<Category>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasIndex(c => c.Name).IsUnique();
            e.Property(c => c.Name).IsRequired().HasMaxLength(100);
        });

        builder.Entity<Case>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Subject).IsRequired().HasMaxLength(300);
            e.Property(c => c.CaseDisplayId).HasMaxLength(20);
            e.Property(c => c.ResolvedAtUtc);
            e.HasOne(c => c.Category!).WithMany(c => c.Cases)
                .HasForeignKey(c => c.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(c => c.AssignedToUser!).WithMany()
                .HasForeignKey(c => c.AssignedToUserId).OnDelete(DeleteBehavior.SetNull);
            e.HasQueryFilter(c => !c.IsDeleted);
        });

        builder.Entity<CaseComment>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).ValueGeneratedOnAdd();
            e.Property(c => c.Body).IsRequired().HasMaxLength(4000);
            e.HasOne(c => c.Case!).WithMany(c => c.Comments)
                .HasForeignKey(c => c.CaseId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(c => c.AuthorUser!).WithMany()
                .HasForeignKey(c => c.AuthorUserId).OnDelete(DeleteBehavior.SetNull);
            // NO ACTION (not SetNull): SQL Server forbids multiple cascade paths
            // to Customers (Case -> Customer is Cascade, so a second path via
            // CaseComments.AuthorCustomerId would error). A customer with
            // comments is simply not deletable until their comments are removed.
            e.HasOne(c => c.AuthorCustomer!).WithMany()
                .HasForeignKey(c => c.AuthorCustomerId).OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<CallLog>(e =>
        {
            e.HasKey(l => l.Id);
            e.HasOne(l => l.Case!).WithMany(c => c.CallLogs)
                .HasForeignKey(l => l.CaseId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Notification>(e =>
        {
            e.HasKey(n => n.Id);
            e.Property(n => n.Title).IsRequired().HasMaxLength(200);
            e.Property(n => n.Message).IsRequired().HasMaxLength(1000);
            e.Property(n => n.Link).HasMaxLength(200);
            // Notifications reference a case but must survive case deletion.
            e.HasOne(n => n.Case).WithMany(c => c.Notifications)
                .HasForeignKey(n => n.CaseId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<CustomerActivity>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.Kind).IsRequired().HasMaxLength(50);
            e.Property(a => a.Label).IsRequired().HasMaxLength(100);
            e.Property(a => a.Detail).HasMaxLength(500);
            e.Property(a => a.ActorUserId).HasMaxLength(100);
            e.Property(a => a.ActorRole).HasMaxLength(50);
            // CaseId is nullable: null for account-only events, set for case-level
            // lifecycle events (case_deleted / case_restored) so the case activity
            // panel can filter this unified audit table by CaseId. No FK — the
            // audit row must survive the case being soft-deleted/restored.
            e.Property(a => a.CaseId).IsRequired(false);
            e.HasOne(a => a.Customer!).WithMany()
                .HasForeignKey(a => a.CustomerId).OnDelete(DeleteBehavior.Cascade);
        });

        // Viewed/opened audit rows. Stored as a discriminator table (no FK to
        // Case/Customer) so the log survives target deletion and needs no
        // migration. Indexes support the per-target and per-target+viewer
        // cooldown lookups in ViewEventService.
        builder.Entity<ViewEvent>(e =>
        {
            e.HasKey(v => v.Id);
            e.Property(v => v.TargetType).IsRequired().HasMaxLength(20);
            e.Property(v => v.ViewerName).IsRequired().HasMaxLength(200);
            e.Property(v => v.ViewerUserId).HasMaxLength(100);
            e.Property(v => v.ViewerRole).HasMaxLength(50);
            e.HasIndex(v => new { v.TargetType, v.TargetId });
            e.HasIndex(v => new { v.TargetType, v.TargetId, v.ViewerUserId, v.AtUtc });
        });

        builder.Entity<ConversationReadState>(e =>
        {
            e.HasKey(r => r.Id);
            // One marker per agent per case.
            e.HasIndex(r => new { r.AgentUserId, r.CaseId }).IsUnique();
            e.Property(r => r.AgentUserId).IsRequired().HasMaxLength(100);
            // The marker references a case but must not block case deletion.
            e.HasOne<Case>().WithMany().HasForeignKey(r => r.CaseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RefreshToken>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).ValueGeneratedOnAdd();
            e.HasIndex(r => r.Token).IsUnique();
            e.Property(r => r.Token).IsRequired().HasMaxLength(128);
            e.Property(r => r.SubjectId).IsRequired().HasMaxLength(100);
            e.Property(r => r.SubjectType).IsRequired().HasMaxLength(20);
            e.Property(r => r.Role).IsRequired().HasMaxLength(50);
            e.Property(r => r.ReplacedByToken).HasMaxLength(128);
        });
    }
}
