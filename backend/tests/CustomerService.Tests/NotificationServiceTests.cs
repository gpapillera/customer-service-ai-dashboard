using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
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
        Build()
    {
        var cases = new FakeRepository<Case>();
        var notes = new FakeRepository<Notification>();
        var sender = new FakeSender(notes);
        var svc = new NotificationService(cases, notes, sender);
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

    [Fact]
    public async Task GenerateOverdueAsync_CreatesOneNotificationPerOverdueCase()
    {
        var (svc, cases, _, sender) = Build();
        cases.AddAsync(OverdueCase(2, "Package not delivered", "Maria Clara", 2)).Wait();
        cases.AddAsync(OverdueCase(6, "Integration webhook failing", "Liza Lopez", 3)).Wait();
        // A resolved case must NOT generate a notification.
        var resolved = OverdueCase(9, "Done", "Nobody", 5);
        resolved.Status = CaseStatus.Resolved;
        cases.AddAsync(resolved).Wait();

        var created = await svc.GenerateOverdueAsync();

        Assert.Equal(2, created);
        Assert.Equal(2, sender.Sent.Count);
        Assert.All(sender.Sent, n => Assert.Equal("Overdue follow-up", n.Title));
        Assert.Contains(sender.Sent, n => n.Message.Contains("Package not delivered"));
        Assert.Contains(sender.Sent, n => n.Message.Contains("Integration webhook failing"));
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
}
