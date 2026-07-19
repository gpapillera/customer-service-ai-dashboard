using CustomerService.Domain.Entities;

namespace CustomerService.Domain;

/// <summary>
/// Single source of truth for "does this case need a follow-up / is it overdue".
/// Used by the dashboard, the cases filter, and the notification generator so
/// they never disagree.
///
/// A case is considered to need a follow-up (overdue) when it is still open and
/// EITHER:
///   (a) it has a scheduled follow-up deadline (<see cref="Case.FollowUpDueUtc"/>)
///       that is in the past and no call log happened on/after that deadline, OR
///   (b) it has NO scheduled deadline but has gone <see cref="StaleDays"/> days
///       with no follow-up call log at all (a "stale" case that was never
///       scheduled for a follow-up).
///
/// Both conditions require the case to be open (New / InProgress / Escalated)
/// and to have no qualifying follow-up since the relevant reference point.
/// </summary>
public static class OverduePolicy
{
    /// <summary>Open statuses that can be overdue.</summary>
    public static readonly CaseStatus[] OpenStatuses =
        { CaseStatus.New, CaseStatus.InProgress, CaseStatus.Escalated };

    /// <summary>
    /// Days an open case with no scheduled follow-up may sit with no call log
    /// before it is flagged as a stale follow-up. Only applies when no
    /// <see cref="Case.FollowUpDueUtc"/> is set.
    /// </summary>
    public const int StaleDays = 3;

    /// <summary>
    /// SLA window (in days) used to auto-schedule a follow-up deadline when a
    /// case is created without an explicit one. Higher priority = sooner.
    /// </summary>
    public static int SlaDays(Priority priority) => priority switch
    {
        Priority.High => 1,
        Priority.Medium => 3,
        Priority.Low => 7,
        _ => 3,
    };

    /// <summary>
    /// True when the case is open and needs a follow-up. Two cases:
    ///  - Scheduled: a follow-up deadline exists, it is in the past, and no call
    ///    log happened on/after that deadline.
    ///  - Stale: no deadline is set, and no call log happened in the last
    ///    <see cref="StaleDays"/> days.
    /// A future deadline is not yet due, so the case is not flagged.
    /// </summary>
    public static bool NeedsFollowUp(Case c, DateTime? now = null)
    {
        var t = now ?? DateTime.UtcNow;
        if (!OpenStatuses.Contains(c.Status))
        {
            return false;
        }

        if (c.FollowUpDueUtc.HasValue)
        {
            // Scheduled path: only overdue once the deadline has passed.
            if (c.FollowUpDueUtc.Value >= t)
            {
                return false;
            }

            var followedUp = c.CallLogs.Any(cl => cl.CreatedAtUtc >= c.FollowUpDueUtc.Value);
            return !followedUp;
        }

        // Stale path: no deadline, flag if no follow-up for StaleDays.
        var staleThreshold = t.AddDays(-StaleDays);
        var followedUpStale = c.CallLogs.Any(cl => cl.CreatedAtUtc >= staleThreshold);
        return !followedUpStale;
    }

    /// <summary>
    /// Days past the follow-up reference point (rounded up, minimum 1). For a
    /// scheduled case this is days past the deadline; for a stale case it is
    /// days past the stale threshold.
    /// </summary>
    public static int DaysOverdue(Case c, DateTime? now = null)
    {
        var t = now ?? DateTime.UtcNow;
        var reference = c.FollowUpDueUtc.HasValue
            ? c.FollowUpDueUtc.Value
            : t.AddDays(-StaleDays);
        var days = (int)Math.Ceiling((t - reference).TotalDays);
        return days < 1 ? 1 : days;
    }

    /// <summary>
    /// Computes the follow-up deadline for a newly created case. If an explicit
    /// deadline is supplied it is honoured; otherwise an SLA deadline is derived
    /// from the priority so the case is automatically tracked for follow-up.
    /// </summary>
    public static DateTime? ComputeFollowUpDueUtc(Priority priority, DateTime? explicitDue, DateTime createdAtUtc)
        => explicitDue ?? createdAtUtc.AddDays(SlaDays(priority));
}
