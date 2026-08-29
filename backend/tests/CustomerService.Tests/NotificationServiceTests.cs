using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using CustomerService.Application.Options;
using CustomerService.Application.Services;
using CustomerService.Domain.Entities;
using CustomerService.Tests.Fakes;
using Xunit;

namespace CustomerService.Tests;

/// <summary>
/// Unit tests for <see cref="NotificationService"/>: overdue generation,
/// idempotent de-duplication, and mark-read behaviour. Repositories are faked.
/// </summary>
public class NotificationServiceTests
{
    private static (NotificationService svc, FakeRepository<Case> cases, FakeRepository<Notification> notes, FakeSender sender)
        Build(List<NotificationChannel>? channels = null)
    {
        var cases = new FakeRepository<Case>();
        var notes = new FakeRepository<Notification>();
        var sender = new FakeSender(notes);
        var options = new NotificationOptions { Channels = channels ?? new() { NotificationChannel.InApp } };
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<NotificationService>.Instance;
        var customerAuth = new FakeCustomerAuthService();
        var svc = new NotificationService(cases, notes, sender, options, logger, customerAuth);
        return (svc, cases, notes, sender);
    }

    private static Case OverdueCase(int id, string subject, string customer, int daysOverdue)
    {
        return new Case
        {
            Id = id,
            Subject = subject,
            Customer = new Customer { Id = 1, Name = customer },
            Status = CaseStatus.InProgress,
            FollowUpDueUtc = DateTime.UtcNow.AddDays(-daysOverdue),
            CallLogs = new List<CallLog>(),
        };
    }

    /// <summary>A stale open case with NO scheduled deadline and no call logs.</summary>
    private static Case StaleCase(int id, string subject, string customer, int ageDays)
    {
        return new Case
        {
            Id = id,
            Subject = subject,
            Customer = new Customer { Id = 1, Name = customer },
            Status = CaseStatus.New,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-ageDays),
            CallLogs = new List<CallLog>(),
        };
    }

    [Fact]
    public async Task GenerateOverdueAsync_CreatesOneNotificationPerOverdueCase()
    {
        var (svc, cases, _, sender) = Build();
        await cases.AddAsync(OverdueCase(2, "Package not delivered", "Maria Clara", 2));
        await cases.AddAsync(OverdueCase(6, "Integration webhook failing", "Liza Lopez", 3));
        // A stale open case (no deadline, no follow-up) must also be flagged.
        await cases.AddAsync(StaleCase(13, "Feature request: bulk export", "Mark", 5));
        // A resolved case must NOT generate a notification.
        var resolved = OverdueCase(9, "Done", "Nobody", 5);
        resolved.Status = CaseStatus.Resolved;
        await cases.AddAsync(resolved);

        var created = await svc.GenerateOverdueAsync();

        Assert.Equal(3, created);
        Assert.Equal(3, sender.Sent.Count);
        Assert.All(sender.Sent, n => Assert.Equal("Overdue follow-up", n.Title));
        Assert.Contains(sender.Sent, n => n.Message.Contains("Package not delivered"));
        Assert.Contains(sender.Sent, n => n.Message.Contains("Integration webhook failing"));
        Assert.Contains(sender.Sent, n => n.Message.Contains("Feature request: bulk export"));
    }

    [Fact]
    public async Task GenerateOverdueAsync_IsIdempotent_DoesNotDuplicate()
    {
        var (svc, cases, _, _) = Build();
        await cases.AddAsync(OverdueCase(2, "Package not delivered", "Maria Clara", 2));

        var first = await svc.GenerateOverdueAsync();
        var second = await svc.GenerateOverdueAsync();

        Assert.Equal(1, first);
        Assert.Equal(0, second); // already notified → no new notification
    }

    [Fact]
    public async Task MarkReadAsync_UpdatesStatus_AndMarkAllRead_ClearsAll()
    {
        var (svc, cases, notes, _) = Build();
        await cases.AddAsync(OverdueCase(2, "Package not delivered", "Maria Clara", 2));
        await svc.GenerateOverdueAsync();

        var summary = await svc.GetSummaryAsync();
        Assert.Equal(1, summary.UnreadCount);

        var ok = await svc.MarkReadAsync(1);
        Assert.True(ok);
        summary = await svc.GetSummaryAsync();
        Assert.Equal(0, summary.UnreadCount);

        // Re-generate must NOT recreate a notification for the same (now read) case.
        var again = await svc.GenerateOverdueAsync();
        Assert.Equal(0, again);
    }

    /// <summary>Captures notifications "sent" by the in-app sender.</summary>
    private class FakeSender : INotificationSender
    {
        private readonly FakeRepository<Notification> _notes;
        public List<Notification> Sent { get; } = new();

        public FakeSender(FakeRepository<Notification> notes) => _notes = notes;

        public async Task SendAsync(Notification notification)
        {
            Sent.Add(notification);
            await _notes.AddAsync(notification);
            await _notes.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task GenerateOverdueAsync_CreatesOnePerChannel_WhenEmailEnabled()
    {
        var (svc, cases, _, sender) = Build(new() { NotificationChannel.InApp, NotificationChannel.Email });
        var c = OverdueCase(2, "Package not delivered", "Maria Clara", 2);
        // Overdue emails go to the ASSIGNED AGENT, not the customer.
        c.AssignedToUser = new User { Id = "agent-001", Email = "agent@example.com" };
        c.Customer!.Email = "maria@example.com";
        await cases.AddAsync(c);

        var created = await svc.GenerateOverdueAsync();

        Assert.Equal(2, created);
        Assert.Equal(2, sender.Sent.Count);
        Assert.Contains(sender.Sent, n => n.Channel == NotificationChannel.InApp && n.Recipient == null);
        Assert.Contains(sender.Sent, n => n.Channel == NotificationChannel.Email && n.Recipient == "agent@example.com");
    }

    [Fact]
    public async Task GenerateOverdueAsync_IsIdempotent_PerChannel()
    {
        var (svc, cases, _, _) = Build(new() { NotificationChannel.InApp, NotificationChannel.Email });
        var c = OverdueCase(2, "Package not delivered", "Maria Clara", 2);
        c.AssignedToUser = new User { Id = "agent-001", Email = "agent@example.com" };
        await cases.AddAsync(c);

        var first = await svc.GenerateOverdueAsync();
        var second = await svc.GenerateOverdueAsync();

        Assert.Equal(2, first);
        Assert.Equal(0, second); // already notified on both channels → no new notifications
    }

    [Fact]
    public async Task GenerateOverdueAsync_SkipsUnassignedCase_NoRecipient()
    {
        var (svc, cases, _, sender) = Build(new() { NotificationChannel.Email });
        var c = OverdueCase(2, "Package not delivered", "Maria Clara", 2);
        c.AssignedToUser = null; // unassigned → no agent email
        await cases.AddAsync(c);

        var created = await svc.GenerateOverdueAsync();

        Assert.Equal(0, created);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task NotifyResolvedAsync_SendsToCustomer_AndIsIdempotent()
    {
        var (svc, cases, notes, sender) = Build(new() { NotificationChannel.Email });
        var c = new Case
        {
            Id = 5,
            Subject = "Done",
            Status = CaseStatus.Resolved,
            Customer = new Customer { Id = 1, Name = "Ana", Email = "ana@example.com" },
        };

        var first = await svc.NotifyResolvedAsync(c);
        var second = await svc.NotifyResolvedAsync(c);

        Assert.Equal(1, first);
        Assert.Equal(0, second); // same (CaseId, Email, CaseResolved) → not re-sent
        var sent = Assert.Single(sender.Sent);
        Assert.Equal(NotificationChannel.Email, sent.Channel);
        Assert.Equal(NotificationType.CaseResolved, sent.Type);
        Assert.Equal("ana@example.com", sent.Recipient);
    }

    [Fact]
    public async Task NotifyResolvedAsync_SkipsWhenCustomerHasNoEmail()
    {
        var (svc, cases, _, sender) = Build(new() { NotificationChannel.Email });
        var c = new Case
        {
            Id = 5,
            Subject = "Done",
            Status = CaseStatus.Closed,
            Customer = new Customer { Id = 1, Name = "Ana", Email = string.Empty },
        };

        var created = await svc.NotifyResolvedAsync(c);

        Assert.Equal(0, created);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task OverdueAndResolved_Coexist_OnSameCaseSameChannel()
    {
        // Regression: the de-dup key must be (CaseId, Channel, Type), not just
        // (CaseId, Channel) — otherwise the resolved-customer email would be
        // blocked by the overdue-agent email for the same case.
        var (svc, cases, _, sender) = Build(new() { NotificationChannel.Email });
        var overdue = OverdueCase(2, "Package not delivered", "Maria Clara", 2);
        overdue.AssignedToUser = new User { Id = "agent-001", Email = "agent@example.com" };
        await cases.AddAsync(overdue);

        var overdueCreated = await svc.GenerateOverdueAsync();
        var resolvedCreated = await svc.NotifyResolvedAsync(new Case
        {
            Id = 2,
            Subject = "Package not delivered",
            Status = CaseStatus.Resolved,
            Customer = new Customer { Id = 1, Name = "Maria Clara", Email = "maria@example.com" },
        });

        Assert.Equal(1, overdueCreated);
        Assert.Equal(1, resolvedCreated);
        Assert.Contains(sender.Sent, n => n.Type == NotificationType.CaseOverdue && n.Recipient == "agent@example.com");
        Assert.Contains(sender.Sent, n => n.Type == NotificationType.CaseResolved && n.Recipient == "maria@example.com");
    }

    [Fact]
    public async Task GenerateOverdueAsync_SkipsUnassignedCase_NoAgentEmail()
    {
        // Email audience is the assigned agent; an unassigned case has no
        // recipient and must be skipped (never guessed).
        var (svc, cases, _, sender) = Build(new() { NotificationChannel.Email });
        var c = OverdueCase(2, "Package not delivered", "Maria Clara", 2);
        c.AssignedToUser = null; // unassigned → no agent email
        await cases.AddAsync(c);

        var created = await svc.GenerateOverdueAsync();

        Assert.Equal(0, created);
        Assert.Empty(sender.Sent);
    }

    // ── Resend: account-invite / reset must regenerate a fresh token (not copy the stale Link) ──

    /// <summary>
    /// Records the email passed to the customer-auth resend/reset-by-email paths
    /// so tests can assert ResendEmailAsync routes account emails there instead
    /// of copying the original (stale-token) notification row.
    /// </summary>
    private sealed class RecordingCustomerAuth : ICustomerAuthService
    {
        public string? ResentInviteEmail;
        public string? ResetEmail;

        public Task<string> SendInviteAsync(int customerId) => throw new System.NotImplementedException();
        public Task RegisterAsync(RegisterCustomerDto dto) => throw new System.NotImplementedException();
        public Task<ValidateInviteResponse> ValidateInviteAsync(string token) => throw new System.NotImplementedException();
        public Task AcceptInviteAsync(AcceptInviteRequest request) => throw new System.NotImplementedException();
        public Task<CustomerLoginResponse?> LoginAsync(CustomerLoginRequest request) => throw new System.NotImplementedException();
        public Task<CustomerProfileDto> GetProfileAsync(int customerId) => throw new System.NotImplementedException();
        public Task UpdateProfileAsync(int customerId, UpdateCustomerProfileDto dto) => System.Threading.Tasks.Task.CompletedTask;
        public Task RequestPasswordResetAsync(int customerId) => System.Threading.Tasks.Task.CompletedTask;
        public Task<string> ResendInviteByEmailAsync(string email) { ResentInviteEmail = email; return System.Threading.Tasks.Task.FromResult("fresh-token"); }
        public Task RequestPasswordResetByEmailAsync(string email) { ResetEmail = email; return System.Threading.Tasks.Task.CompletedTask; }
        public Task<(string AccessToken, string RefreshToken, DateTime ExpiresUtc)> RefreshAsync(string refreshToken) => throw new System.NotImplementedException();
        public Task LogoutAsync(string refreshToken) => System.Threading.Tasks.Task.CompletedTask;
    }

    private static NotificationService BuildWith(RecordingCustomerAuth auth, out FakeRepository<Notification> notes)
    {
        var cases = new FakeRepository<Case>();
        notes = new FakeRepository<Notification>();
        var sender = new FakeSender(notes);
        var options = new NotificationOptions { Channels = new() { NotificationChannel.Email } };
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<NotificationService>.Instance;
        return new NotificationService(cases, notes, sender, options, logger, auth);
    }

    [Fact]
    public async Task Resend_CustomerInvite_RoutesToFreshToken_NotStaleLink()
    {
        var auth = new RecordingCustomerAuth();
        var svc = BuildWith(auth, out var notes);
        var stale = new Notification
        {
            Id = 100,
            Channel = NotificationChannel.Email,
            Type = NotificationType.CustomerInvite,
            Recipient = "linktest@example-notreal.test",
            Title = "You've been invited to the Customer Portal",
            Link = "http://localhost:4200/customer/accept-invite?token=STALETOKEN",
        };
        await notes.AddAsync(stale);

        var dto = await svc.ResendEmailAsync(100);

        // The resend must regenerate a fresh token via customer-auth, NOT echo
        // the stale Link back through as a plain copy.
        Assert.Equal("linktest@example-notreal.test", auth.ResentInviteEmail);
        Assert.NotNull(dto);
        Assert.NotEqual("STALETOKEN", dto!.Link ?? string.Empty);
    }

    [Fact]
    public async Task Resend_CustomerPasswordReset_RoutesToFreshToken()
    {
        var auth = new RecordingCustomerAuth();
        var svc = BuildWith(auth, out var notes);
        var stale = new Notification
        {
            Id = 101,
            Channel = NotificationChannel.Email,
            Type = NotificationType.CustomerPasswordReset,
            Recipient = "reset@example.com",
            Title = "Password Reset — Customer Portal",
            Link = "http://localhost:4200/customer/accept-invite?token=OLDTOKEN",
        };
        await notes.AddAsync(stale);

        var dto = await svc.ResendEmailAsync(101);

        Assert.Equal("reset@example.com", auth.ResetEmail);
        Assert.NotNull(dto);
    }

    [Fact]
    public async Task Resend_CaseOverdue_CopiesOriginalVerbatim()
    {
        // Non-token email types must still be copied faithfully (no customer-auth call).
        var auth = new RecordingCustomerAuth();
        var svc = BuildWith(auth, out var notes);
        var original = new Notification
        {
            Id = 102,
            Channel = NotificationChannel.Email,
            Type = NotificationType.CaseOverdue,
            Recipient = "agent@example.com",
            Title = "Case #21 is overdue: Bulk discount not applied",
            Message = "A follow-up on case #21 is overdue.",
            Link = "https://example.com/dashboard/21", // non-token link preserved
        };
        await notes.AddAsync(original);

        var dto = await svc.ResendEmailAsync(102);

        Assert.Null(auth.ResentInviteEmail);
        Assert.NotNull(dto);
        Assert.Equal("https://example.com/dashboard/21", dto!.Link);
        Assert.Equal("A follow-up on case #21 is overdue.", dto.Message);
    }
}
