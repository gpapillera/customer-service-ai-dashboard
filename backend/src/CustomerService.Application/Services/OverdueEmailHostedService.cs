using System.Threading;
using System.Threading.Tasks;
using CustomerService.Application.Interfaces;
using CustomerService.Application.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerService.Application.Services;

/// <summary>
/// Background worker that periodically scans for overdue cases and triggers the
/// agent-facing overdue email. Overdue detection is time-based (not event-based)
/// so nothing in the UI naturally triggers it — this hosted service fills that
/// gap. The interval is configurable via <c>Notifications:OverdueCheckIntervalMinutes</c>
/// (default 30). Each run is idempotent: a notification is only created when no
/// (CaseId, Email, CaseOverdue) row already exists, so re-runs never re-send.
/// </summary>
public class OverdueEmailHostedService : IHostedService
{
    private readonly ILogger<OverdueEmailHostedService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NotificationOptions _options;
    private Timer? _timer;

    /// <summary>Initializes a new <see cref="OverdueEmailHostedService"/>.</summary>
    /// <param name="logger">Logger.</param>
    /// <param name="scopeFactory">Scope factory (each run gets its own scope).</param>
    /// <param name="options">Notification options (interval + channels).</param>
    public OverdueEmailHostedService(
        ILogger<OverdueEmailHostedService> logger,
        IServiceScopeFactory scopeFactory,
        IOptions<NotificationOptions> options)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _options = options.Value;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromMinutes(_options.OverdueCheckIntervalMinutes);
        _logger.LogInformation(
            "Overdue email worker starting; first run in {FirstRun}, then every {Interval}.",
            TimeSpan.FromSeconds(15), interval);
        // Fire once shortly after startup, then on the configured interval.
        _timer = new Timer(_ => DoWork(), null, TimeSpan.FromSeconds(15), interval);
        return Task.CompletedTask;
    }

    private void DoWork()
    {
        // Run outside the timer callback's sync context; never let a failure
        // take down the worker or the host.
        _ = Task.Run(async () =>
        {
            try
            {
                // Resolve scoped services per run (the worker itself is a singleton).
                await using var scope = _scopeFactory.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<INotificationService>();
                var created = await svc.GenerateOverdueAsync();
                if (created > 0)
                {
                    _logger.LogInformation("Overdue email worker created {Count} notification(s).", created);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Overdue email worker failed on a scheduled run.");
            }
        });
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Overdue email worker stopping.");
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }
}
