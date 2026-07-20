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
        var svc = new NotificationService(cases, notes, sender, options, logger);
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
        cases.AddAsync(OverdueCase(2, "Package not delivered", "Maria Clara", 2)).Wait();
        cases.AddAsync(OverdueCase(6, "Integration webhook failing", "Liza Lopez", 3)).Wait();
        // A stale open case (no deadline, no follow-up) must also be flagged.
        cases.AddAsync(StaleCase(13, "Feature request: bulk export", "Mark", 5)).Wait();
        // A resolved case must NOT generate a notification.
        var resolved = OverdueCase(9, "Done", "Nobody", 5);
        resolved.Status = CaseStatus.Resolved;
        cases.AddAsync(resolved).Wait();

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
        cases.AddAsync(OverdueCase(2, "Package not delivered", "Maria Clara", 2)).Wait();

        var first = await svc.GenerateOverdueAsync();
        var second = await svc.GenerateOverdueAsync();

        Assert.Equal(1, first);
        Assert.Equal(0, second); // already notified → no new notification
    }

    [Fact]
    public async Task MarkReadAsync_UpdatesStatus_AndMarkAllRead_ClearsAll()
    {
        var (svc, cases, notes, _) = Build();
        cases.AddAsync(OverdueCase(2, "Package not delivered", "Maria Clara", 2)).Wait();
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
    public async Task GenerateOverdueAsync_CreatesOnePerChannel_WhenEmailAndSmsEnabled()
    {
        var (svc, cases, _, sender) = Build(new() { NotificationChannel.InApp, NotificationChannel.Email, NotificationChannel.Sms });
        var c = OverdueCase(2, "Package not delivered", "Maria Clara", 2);
        // Overdue emails go to the ASSIGNED AGENT, not the customer.
        c.AssignedToUser = new User { Id = "agent-001", Email = "agent@example.com" };
        c.Customer!.Email = "maria@example.com";
        c.Customer!.Phone = "+15551234567";
        cases.AddAsync(c).Wait();

        var created = await svc.GenerateOverdueAsync();

        Assert.Equal(3, created);
        Assert.Equal(3, sender.Sent.Count);
        Assert.Contains(sender.Sent, n => n.Channel == NotificationChannel.InApp && n.Recipient == null);
        Assert.Contains(sender.Sent, n => n.Channel == NotificationChannel.Email && n.Recipient == "agent@example.com");
        Assert.Contains(sender.Sent, n => n.Channel == NotificationChannel.Sms && n.Recipient == "+15551234567");
    }

    [Fact]
    public async Task GenerateOverdueAsync_IsIdempotent_PerChannel()
    {
        var (svc, cases, _, _) = Build(new() { NotificationChannel.InApp, NotificationChannel.Email });
        var c = OverdueCase(2, "Package not delivered", "Maria Clara", 2);
        c.AssignedToUser = new User { Id = "agent-001", Email = "agent@example.com" };
        cases.AddAsync(c).Wait();

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
        cases.AddAsync(c).Wait();

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
        cases.AddAsync(overdue).Wait();

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
    public async Task GenerateOverdueAsync_SmsGoesToCustomerPhone_NotAgentEmail()
    {
        // SMS audience is unchanged (customer phone), even though Email goes to
        // the agent. Regression guard for the recipient-resolution switch.
        var (svc, cases, _, sender) = Build(new() { NotificationChannel.Sms });
        var c = OverdueCase(2, "Package not delivered", "Maria Clara", 2);
        c.AssignedToUser = new User { Id = "agent-001", Email = "agent@example.com" };
        c.Customer!.Phone = "+15551234567";
        cases.AddAsync(c).Wait();

        var created = await svc.GenerateOverdueAsync();

        Assert.Equal(1, created);
        var sent = Assert.Single(sender.Sent);
        Assert.Equal(NotificationChannel.Sms, sent.Channel);
        Assert.Equal("+15551234567", sent.Recipient);
    }
}
