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

    /// <summary>Per-agent, per-case "last viewed" markers for the Messages tab.</summary>
    public DbSet<ConversationReadState> ConversationReadStates => Set<ConversationReadState>();

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
            e.Property(c => c.ResolvedAtUtc);
            e.HasOne(c => c.Category!).WithMany(c => c.Cases)
                .HasForeignKey(c => c.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(c => c.AssignedToUser!).WithMany()
                .HasForeignKey(c => c.AssignedToUserId).OnDelete(DeleteBehavior.SetNull);
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
            e.HasOne<Case>().WithMany().HasForeignKey(n => n.CaseId)
                .OnDelete(DeleteBehavior.SetNull);
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
    }
}
