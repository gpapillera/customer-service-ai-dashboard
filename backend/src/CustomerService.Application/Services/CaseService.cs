using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using CustomerService.Domain;
using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;
using CustomerService.ML;
using Microsoft.EntityFrameworkCore;

namespace CustomerService.Application.Services;

/// <summary>
/// Implements <see cref="ICaseService"/>. On creation, when no priority is
/// supplied, the ML predictor suggests one (flagged as auto-suggested).
/// See docs/DIY.md §9 for the priority-prediction wiring in CreateAsync.
/// </summary>
public class CaseService : ICaseService
{
    private readonly IRepository<Case> _cases;
    private readonly IRepository<Customer> _customers;
    private readonly IRepository<Category> _categories;
    private readonly IRepository<CaseComment> _comments;
    private readonly IRepository<ConversationReadState> _readStates;
    private readonly IRepository<CustomerActivity> _activities;
    private readonly IPriorityPredictor _predictor;
    private readonly INotificationService _notifications;

    /// <summary>Initializes a new <see cref="CaseService"/>.</summary>
    /// <param name="cases">Case repository.</param>
    /// <param name="customers">Customer repository.</param>
    /// <param name="categories">Category repository.</param>
    /// <param name="comments">Case-comment repository (for conversation summaries).</param>
    /// <param name="readStates">Per-agent per-case "last viewed" markers.</param>
    /// <param name="predictor">Priority predictor (ML or rule-based fallback).</param>
    /// <param name="notifications">Notification service (resolved/customer email).</param>
    public CaseService(
        IRepository<Case> cases,
        IRepository<Customer> customers,
        IRepository<Category> categories,
        IRepository<CaseComment> comments,
        IRepository<ConversationReadState> readStates,
        IRepository<CustomerActivity> activities,
        IPriorityPredictor predictor,
        INotificationService notifications,
        ILiveUpdateHub events)
    {
        _cases = cases;
        _customers = customers;
        _categories = categories;
        _comments = comments;
        _readStates = readStates;
        _activities = activities;
        _predictor = predictor;
        _notifications = notifications;
        _events = events;
    }

    private readonly ILiveUpdateHub _events;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CaseDto>> GetAllAsync(
        CaseStatus? status, Priority? priority, int? categoryId, DateTime? from, DateTime? to, bool overdue = false, string? assignedToUserId = null, string? callerRole = null, string? callerUserId = null, bool unassigned = false)
    {
        IQueryable<Case> q = _cases.Query()
            .Include(c => c.Customer)
            .Include(c => c.Category)
            .Include(c => c.CallLogs)
            .Include(c => c.Comments)
            .AsSplitQuery();

        // SERVER-SIDE AGENT SCOPING (Phase 6). An Agent may only ever see cases
        // assigned to them OR unassigned — regardless of any query param. This
        // is the real security boundary; the UI cannot widen it. Admin is
        // unaffected and sees everything.
        var isAgent = string.Equals(callerRole, nameof(UserRole.Agent), StringComparison.OrdinalIgnoreCase);
        if (isAgent && !string.IsNullOrEmpty(callerUserId))
        {
            q = q.Where(c => c.AssignedToUserId == callerUserId || c.AssignedToUserId == null);
        }

        if (status.HasValue) q = q.Where(c => c.Status == status.Value);
        if (priority.HasValue) q = q.Where(c => c.Priority == priority.Value);
        if (categoryId.HasValue) q = q.Where(c => c.CategoryId == categoryId.Value);
        if (from.HasValue) q = q.Where(c => c.CreatedAtUtc >= from.Value);
        if (to.HasValue) q = q.Where(c => c.CreatedAtUtc <= to.Value);
        // "Assigned to me" — resolved from the JWT by the controller, never
        // trusted from the client. Enables the Agent dashboard click-through.
        // For an Agent it further narrows their already-restricted view to
        // theirs-only; it can never widen beyond the base restriction above.
        if (!string.IsNullOrEmpty(assignedToUserId))
        {
            q = q.Where(c => c.AssignedToUserId == assignedToUserId);
        }
        if (unassigned)
        {
            q = q.Where(c => c.AssignedToUserId == null);
        }
        if (overdue)
        {
            // Open cases that need a follow-up: either a scheduled deadline was
            // missed (deadline in the past, no follow-up since), or (no deadline
            // set) the case has gone StaleDays with no follow-up. Mirrors
            // OverduePolicy.NeedsFollowUp (kept inline here because EF Core
            // cannot translate calls to a custom static method).
            var now = DateTime.UtcNow;
            var staleThreshold = now.AddDays(-OverduePolicy.StaleDays);
            q = q.Where(c => OverduePolicy.OpenStatuses.Contains(c.Status))
                .Where(c =>
                    (c.FollowUpDueUtc.HasValue
                        && c.FollowUpDueUtc.Value < now
                        && !c.CallLogs.Any(cl => cl.CreatedAtUtc >= c.FollowUpDueUtc.Value))
                    || (!c.FollowUpDueUtc.HasValue
                        && !c.CallLogs.Any(cl => cl.CreatedAtUtc >= staleThreshold)));
        }

        return await q.OrderByDescending(c => c.CreatedAtUtc)
            .Select(c => ToDto(c))
            .ToListAsync();
    }

    /// <inheritdoc/>
    public async Task<CaseDto?> GetByIdAsync(int id, string? callerRole = null, string? callerUserId = null)
    {
        // Admins reach deleted-detail views (recycle drawer) and must be able
        // to load a soft-deleted row; other roles keep the global filter so
        // binned rows stay hidden.
        var q = _cases.Query();
        if (string.Equals(callerRole, nameof(UserRole.Admin), StringComparison.OrdinalIgnoreCase))
            q = q.IgnoreQueryFilters();
        var c = await q
            .Include(c => c.Customer)
            .Include(c => c.Category)
            .Include(c => c.AssignedToUser)
            .Include(c => c.CallLogs)
            .Include(c => c.Comments)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return null;

        // Defense-in-depth: even though the list is already filtered, a direct
        // id request by an Agent for a case assigned to another agent must be
        // blocked (403), not just hidden from the list.
        var isAgent = string.Equals(callerRole, nameof(UserRole.Agent), StringComparison.OrdinalIgnoreCase);
        if (isAgent && !string.IsNullOrEmpty(callerUserId)
            && c.AssignedToUserId is not null && c.AssignedToUserId != callerUserId)
        {
            throw new ForbiddenException("You can only view cases assigned to you.");
        }

        return ToDto(c);
    }

    /// <inheritdoc/>
    public async Task<CaseDto> CreateAsync(CreateCaseDto dto)
    {
        if (await _customers.GetByIdAsync(dto.CustomerId) is null)
            throw new KeyNotFoundException($"Customer {dto.CustomerId} not found.");
        if (await _categories.GetByIdAsync(dto.CategoryId) is null)
            throw new KeyNotFoundException($"Category {dto.CategoryId} not found.");

        var priorCaseCount = await _cases.Query().CountAsync(c => c.CustomerId == dto.CustomerId);
        var daysSince = dto.LastContactUtc.HasValue
            ? (int)(DateTime.UtcNow - dto.LastContactUtc.Value).TotalDays
            : 999;
        var sentiment = RuleBasedPriorityPredictor.SentimentScore(dto.Description);

        var prediction = dto.Priority.HasValue
            ? null
            : _predictor.PredictWithReason(new PriorityFeatures
            {
                CategoryId = dto.CategoryId,
                PriorCaseCount = priorCaseCount,
                DaysSinceLastContact = daysSince,
                Sentiment = sentiment,
            });
        var priority = dto.Priority ?? prediction!.Priority;

        var createdAt = DateTime.UtcNow;
        var caseEntity = new Case
        {
            Subject = dto.Subject,
            Description = dto.Description,
            CategoryId = dto.CategoryId,
            CustomerId = dto.CustomerId,
            AssignedToUserId = dto.AssignedToUserId,
            // Stamp the assignment time when a case is created already-assigned.
            AssignedAtUtc = dto.AssignedToUserId is not null ? createdAt : null,
            Status = CaseStatus.New,
            Priority = priority,
            PriorityAutoSuggested = !dto.Priority.HasValue,
            PriorityReason = prediction?.Reason,
            LastContactUtc = dto.LastContactUtc,
            CreatedAtUtc = createdAt,
            // Auto-schedule a follow-up deadline from the SLA so the case is
            // tracked for follow-up even when the UI doesn't set one.
            FollowUpDueUtc = OverduePolicy.ComputeFollowUpDueUtc(priority, null, createdAt),
        };
        await _cases.AddAsync(caseEntity);
        await _cases.SaveChangesAsync();

        // Generate a human-readable display ID after the entity is saved (Id is now set).
        caseEntity.CaseDisplayId = $"CAS-{caseEntity.Id:D5}";
        _cases.Update(caseEntity);
        await _cases.SaveChangesAsync();

        // Push a "case-update" so any open case list/board/dashboard reflects the
        // new case instantly (no manual refresh needed).
        try
        {
            await _events.PublishAsync(new LiveUpdateEvent(
                "case-update", CaseId: caseEntity.Id, ActorRole: "System"));
        }
        catch
        {
            // Swallow — realtime is best-effort.
        }

        return ToDto(caseEntity);
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(int id, UpdateCaseDto dto, string? callerRole = null, string? callerUserId = null)
    {
        var caseEntity = await _cases.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Case {id} not found.");

        var isAgent = string.Equals(callerRole, nameof(UserRole.Agent), StringComparison.OrdinalIgnoreCase);

        // Capture the prior assignee so we can detect an assignment change and
        // broadcast it over SSE for instant UI reflection (no 30s poll lag).
        var priorAssignee = caseEntity.AssignedToUserId;

        // AGENT WRITE SCOPING (Phase 6). Agents may only modify a case that is
        // assigned to them. Unassigned cases are visible but read-only; cases
        // assigned to another agent are neither visible nor writable.
        if (isAgent && !string.IsNullOrEmpty(callerUserId))
        {
            if (caseEntity.AssignedToUserId is null)
            {
                throw new ForbiddenException("You can view unassigned cases but cannot modify them.");
            }
            if (caseEntity.AssignedToUserId != callerUserId)
            {
                throw new ForbiddenException("You can only edit cases assigned to you.");
            }
        }

        // Capture the prior status so we can detect a transition into a
        // resolved/closed state (the trigger for the customer email).
        var priorStatus = caseEntity.Status;

        caseEntity.Subject = dto.Subject;
        caseEntity.Description = dto.Description;
        caseEntity.Status = dto.Status;
        caseEntity.Priority = dto.Priority;
        caseEntity.CategoryId = dto.CategoryId;

        // ASSIGNEE HANDLING: the DTO is a plain nullable string, so it cannot
        // distinguish "field omitted" from "explicitly unassign". We therefore
        // use three cases:
        //  - null            -> preserve the existing assignee (data-loss fix;
        //                       the quick "Update Status"/"Set Priority" actions
        //                       send null because they don't touch assignment).
        //  - UnassignSentinel -> explicitly clear the assignee (the Unassign UI).
        //  - any other value -> set/reassign to that agent id.
        // REASSIGNMENT RULE (Phase 6): an Agent may never change the assignee
        // (reassignment is Admin-only). Any attempt to set a different agent or
        // to clear the assignee is rejected with 403 rather than silently
        // ignored. Admin is unaffected and may reassign freely.
        if (isAgent && !string.IsNullOrEmpty(callerUserId))
        {
            var wantsUnassign = dto.AssignedToUserId == UpdateCaseDto.UnassignSentinel;
            var wantsReassign = dto.AssignedToUserId is not null && dto.AssignedToUserId != callerUserId;
            if (wantsUnassign || wantsReassign)
            {
                throw new ForbiddenException("Reassigning or unassigning a case is restricted to administrators.");
            }
        }
        else
        {
            if (dto.AssignedToUserId == UpdateCaseDto.UnassignSentinel)
            {
                caseEntity.AssignedToUserId = null;
                caseEntity.AssignedAtUtc = null;
            }
            else if (dto.AssignedToUserId is not null)
            {
                caseEntity.AssignedToUserId = dto.AssignedToUserId;
                caseEntity.AssignedAtUtc = DateTime.UtcNow;
            }
        }

        caseEntity.PriorityAutoSuggested = false; // manual override

        // RECALCULATE SLA: when the priority changes while the case is still
        // open, recompute the follow-up deadline so the SLA window reflects the
        // new priority (e.g. escalation from Low → High tightens the window).
        if (caseEntity.Priority != dto.Priority
            && OverduePolicy.OpenStatuses.Contains(caseEntity.Status))
        {
            caseEntity.FollowUpDueUtc = OverduePolicy.ComputeFollowUpDueUtc(
                dto.Priority, null, caseEntity.CreatedAtUtc);
        }

        caseEntity.UpdatedAtUtc = DateTime.UtcNow;
        // Clearing the overdue marker here ends the overdue episode so a future
        // re-open can notify again (Phase 44). Folded into the existing save below.
        if (priorStatus != dto.Status
            && (dto.Status == CaseStatus.Resolved || dto.Status == CaseStatus.Closed))
        {
            caseEntity.LastOverdueNotifiedUtc = null;
        }
        _cases.Update(caseEntity);
        await _cases.SaveChangesAsync();

        // Broadcast an assignment change (assign/reassign/unassign) over SSE so
        // the agent/admin UI reflects it instantly. Skip when the assignee is
        // unchanged — most updates (status, priority, description) don't affect
        // assignment and shouldn't spam the stream. Fire-and-forget: a hub
        // failure must never roll back the committed update. The Kind
        // "case-assignment" keeps the legacy frame name alive for one release;
        // the controller also echoes a unified "live-update" frame.
        if (!string.Equals(priorAssignee, caseEntity.AssignedToUserId, StringComparison.Ordinal))
        {
            try
            {
                await _events.PublishAsync(new LiveUpdateEvent(
                    "case-assignment",
                    CaseId: caseEntity.Id,
                    AssignedToUserId: caseEntity.AssignedToUserId,
                    ActorUserId: callerUserId,
                    ActorRole: callerRole));
            }
            catch
            {
                // Swallow — realtime is best-effort.
            }
        }
        // Any case mutation (status/priority/subject/desc/comment) is worth a
        // "case-update" push so open case lists/boards and the dashboard reflect
        // it instantly. Always emitted (assignment changes also push this).
        try
        {
            await _events.PublishAsync(new LiveUpdateEvent(
                "case-update",
                CaseId: caseEntity.Id,
                ActorUserId: callerUserId,
                ActorRole: callerRole));
        }
        catch
        {
            // Swallow — realtime is best-effort.
        }

        // EVENT-BASED trigger: when a case transitions INTO Resolved/Closed,
        // notify the customer by email (Email channel only, when enabled).
        // Wrapped so a delivery failure never rolls back the status update
        // that already succeeded above.
        if (priorStatus != dto.Status
            && (dto.Status == CaseStatus.Resolved || dto.Status == CaseStatus.Closed))
        {
            try
            {
                // Re-load with customer so the email can resolve the recipient.
                var withCustomer = await _cases.Query()
                    .Include(c => c.Customer)
                    .FirstOrDefaultAsync(c => c.Id == id);
                if (withCustomer is not null)
                {
                    await _notifications.NotifyResolvedAsync(withCustomer);
                }
            }
            catch (Exception ex)
            {
                // Swallow: the status change already committed. Log and move on.
                // (No ILogger injected here; the sender logs its own failures.)
                _ = ex;
            }
        }
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(int id, string? callerRole = null, string? callerUserId = null)
    {
        // Only Admin may delete cases.
        if (!string.Equals(callerRole, nameof(UserRole.Admin), StringComparison.OrdinalIgnoreCase))
            throw new ForbiddenException("Only admins can delete cases.");

        var caseEntity = await _cases.QueryTracked()
            .Include(c => c.Comments)
            .Include(c => c.CallLogs)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new KeyNotFoundException($"Case {id} not found.");

        // Soft-delete: flip the flag instead of physically removing the row.
        // Any comment authorship links that would otherwise dangle (the
        // AuthorCustomerId FK uses NoAction, so EF cannot cascade) are
        // nullified — the comment text is preserved. The case row remains.
        if (caseEntity.Comments is not null)
        {
            foreach (var comment in caseEntity.Comments)
            {
                if (comment.AuthorCustomerId == id)
                    comment.AuthorCustomerId = null;
            }
        }

        caseEntity.IsDeleted = true;
        caseEntity.DeletedAtUtc = DateTime.UtcNow;
        caseEntity.DeletedById = callerUserId;

        await _cases.SaveChangesAsync();

        // Audit: record the case deletion in the unified activity log (keyed to
        // its owning customer + the case id) so it shows on BOTH the customer's
        // activity panel and the case's own activity panel.
        if (caseEntity.CustomerId != 0)
        {
            await _activities.AddAsync(new CustomerActivity
            {
                CustomerId = caseEntity.CustomerId,
                CaseId = caseEntity.Id,
                Kind = "case_deleted",
                Label = "Case deleted",
                Detail = $"\"{caseEntity.Subject}\" moved to recycle bin{(callerUserId != null ? $" by {callerUserId}" : "")}.",
                AtUtc = DateTime.UtcNow,
                ActorUserId = callerUserId,
                ActorRole = callerRole,
            });
            await _activities.SaveChangesAsync();
        }

        // Push a "case-update" so any open case list/board/dashboard reflects the
        // deletion instantly (the row is now soft-deleted; lists should drop it).
        try
        {
            await _events.PublishAsync(new LiveUpdateEvent(
                "case-update", CaseId: caseEntity.Id, ActorUserId: callerUserId, ActorRole: callerRole));
        }
        catch
        {
            // Swallow — realtime is best-effort.
        }
    }

    /// <inheritdoc/>
    public async Task RestoreCaseAsync(int caseId, string? callerUserId = null)
    {
        // Bypass the global soft-delete filter so a binned case can be loaded.
        // Include the owning customer so the account gate below can be checked.
        // QueryTracked() so the IsDeleted flips are persisted.
        var caseEntity = await _cases.QueryTracked()
            .IgnoreQueryFilters()
            .Include(c => c.Customer)
            .FirstOrDefaultAsync(c => c.Id == caseId && c.IsDeleted && !c.Purged)
            ?? throw new KeyNotFoundException("Case is not in the recycle bin (or already purged).");

        // GATE: a case may only be restored if its owning customer account is
        // active. A soft-deleted customer's cases stay unrecoverable until the
        // account itself is restored first.
        if (caseEntity.Customer is not null && caseEntity.Customer.IsDeleted)
            throw new InvalidOperationException("Restore the customer account before restoring its cases.");

        caseEntity.IsDeleted = false;
        caseEntity.DeletedAtUtc = null;
        caseEntity.DeletedById = null;
        await _cases.SaveChangesAsync();

        // Audit: record the case restoration in the unified activity log (keyed
        // to its owning customer + the case id) so it shows on BOTH the
        // customer's activity panel and the case's own activity panel.
        if (caseEntity.CustomerId != 0)
        {
            await _activities.AddAsync(new CustomerActivity
            {
                CustomerId = caseEntity.CustomerId,
                CaseId = caseEntity.Id,
                Kind = "case_restored",
                Label = "Case restored",
                Detail = $"\"{caseEntity.Subject}\" restored from recycle bin{(callerUserId != null ? $" by {callerUserId}" : "")}.",
                AtUtc = DateTime.UtcNow,
                ActorUserId = callerUserId,
                ActorRole = null,
            });
            await _activities.SaveChangesAsync();
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CustomerActivityItemDto>> GetCaseActivityAsync(int caseId)
    {
        // Lifecycle events for this case live in the unified CustomerActivities
        // audit table (case_deleted / case_restored), keyed by CaseId. No global
        // soft-delete filter applies to that table, so these rows survive the
        // case being binned/restored. Returned newest-first to merge cleanly
        // with the case-graph events the client computes locally.
        var rows = await _activities.Query()
            .Where(a => a.CaseId == caseId)
            .OrderByDescending(a => a.AtUtc)
            .ToListAsync();

        return rows.Select(a => new CustomerActivityItemDto
        {
            Id = -3000 - a.Id, // negative, distinct from customer/account kinds
            Kind = a.Kind,
            Label = a.Label,
            Detail = a.Detail,
            AtUtc = a.AtUtc,
            CaseId = a.CaseId,
            Who = a.ActorRole,
        }).ToList();
    }

    /// <inheritdoc/>
    public async Task PurgeCaseAsync(int caseId, string? callerRole = null)
    {
        // Only Admin may purge (anonymize) a soft-deleted case.
        if (!string.Equals(callerRole, nameof(UserRole.Admin), StringComparison.OrdinalIgnoreCase))
            throw new ForbiddenException("Only admins can purge cases.");

        // Bypass the global soft-delete filter so a binned (but not yet purged)
        // case can be loaded. QueryTracked() so the anonymization is persisted.
        var c = await _cases.QueryTracked()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == caseId && x.IsDeleted && !x.Purged);
        if (c is null)
            throw new KeyNotFoundException("Case is not in the recycle bin (or already purged).");

        // Hard purge = KEEP THE ROW but ANONYMIZE: scrub the subject and
        // description and mark the case purged. No physical delete.
        c.Subject = "[deleted]";
        c.Description = "[deleted]";
        c.Purged = true;
        c.PurgedAtUtc = DateTime.UtcNow;

        await _cases.SaveChangesAsync();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CaseDto>> GetDeletedAsync()
    {
        // Bypass the global soft-delete filter and return only binned,
        // not-yet-purged cases with their owning customer for the drawer's
        // "Deleted User (C-00012)" context and the restore-gating hint.
        // Also exclude cases whose owning customer is purged: a purged customer
        // is unrecoverable, so such cases can never be restored and must not
        // show as a dead-end in the bin (defense-in-depth over the purge
        // cascade in CustomerService.PurgeAsync).
        var binned = await _cases.Query()
            .IgnoreQueryFilters()
            .Include(c => c.Customer)
            .Include(c => c.Category)
            .Where(c => c.IsDeleted && !c.Purged)
            .Where(c => c.Customer == null || !c.Customer.Purged)
            .OrderByDescending(c => c.DeletedAtUtc)
            .ToListAsync();

        return binned.Select(c =>
        {
            var dto = ToDto(c);
            dto.IsDeleted = true;
            dto.DeletedAtUtc = c.DeletedAtUtc;
            dto.DeletedById = c.DeletedById;
            dto.Purged = c.Purged;
            // Restore-gating: a case can only be revived once its account is.
            dto.CustomerIsDeleted = c.Customer is not null && c.Customer.IsDeleted;
            dto.CustomerIsPurged = c.Customer is not null && c.Customer.Purged;
            // After purge the customer's name is "Deleted User"; surface a
            // readable customer label either way.
            dto.CustomerName = c.Customer is not null ? c.Customer.Name : "Deleted User";
            return dto;
        }).ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ConversationSummaryDto>> GetMyConversationsAsync(string agentUserId)
    {
        if (string.IsNullOrWhiteSpace(agentUserId))
        {
            return Array.Empty<ConversationSummaryDto>();
        }

        // Cases assigned to this agent that have at least one comment.
        var assignedCaseIds = await _cases.Query()
            .Where(c => c.AssignedToUserId == agentUserId)
            .Select(c => c.Id)
            .ToListAsync();

        if (assignedCaseIds.Count == 0)
        {
            return Array.Empty<ConversationSummaryDto>();
        }

        // Latest comment per case (with author name), plus the agent's
        // last-viewed marker for that case.
        var latestComments = await _comments.Query()
            .Include(cm => cm.AuthorUser)
            .Include(cm => cm.AuthorCustomer)
            .Where(cm => assignedCaseIds.Contains(cm.CaseId))
            .GroupBy(cm => cm.CaseId)
            .Select(g => g.OrderByDescending(cm => cm.CreatedAtUtc).First())
            .ToListAsync();

        var readStates = await _readStates.Query()
            .Where(r => r.AgentUserId == agentUserId && assignedCaseIds.Contains(r.CaseId))
            .ToDictionaryAsync(r => r.CaseId, r => r.LastViewedUtc);

        // Latest NON-SELF comment per case — used for unread detection so
        // a user's own reply never makes the conversation appear unread to them.
        var latestNonSelfComments = await _comments.Query()
            .Where(cm => assignedCaseIds.Contains(cm.CaseId) && cm.AuthorUserId != agentUserId)
            .GroupBy(cm => cm.CaseId)
            .Select(g => new { CaseId = g.Key, LatestAt = g.Max(cm => cm.CreatedAtUtc) })
            .ToDictionaryAsync(x => x.CaseId, x => x.LatestAt);

        // All non-self comments with timestamps — used to count the actual
        // number of unread messages per conversation (vs the boolean flag).
        var allNonSelfComments = await _comments.Query()
            .Where(cm => assignedCaseIds.Contains(cm.CaseId) && cm.AuthorUserId != agentUserId)
            .Select(cm => new { cm.CaseId, cm.CreatedAtUtc })
            .ToListAsync();

        var nonSelfByCase = allNonSelfComments
            .GroupBy(cm => cm.CaseId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Batch-load all cases in one query to avoid N+1 per-case lookups.
        var caseEntities = await _cases.Query()
            .Include(c => c.Customer)
            .Where(c => assignedCaseIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id);

        var result = new List<ConversationSummaryDto>();
        foreach (var comment in latestComments)
        {
            if (!caseEntities.TryGetValue(comment.CaseId, out var caseEntity))
            {
                continue;
            }

            var lastViewed = readStates.TryGetValue(comment.CaseId, out var v) ? v : DateTime.MinValue;
            var latestNonSelfAt = latestNonSelfComments.TryGetValue(comment.CaseId, out var t) ? t : DateTime.MinValue;
            var unread = latestNonSelfAt > lastViewed;

            var unreadCount = 0;
            if (nonSelfByCase.TryGetValue(comment.CaseId, out var caseComments))
            {
                unreadCount = caseComments.Count(cm => cm.CreatedAtUtc > lastViewed);
            }

            result.Add(new ConversationSummaryDto
            {
                CaseId = comment.CaseId,
                CaseDisplayId = caseEntity.CaseDisplayId,
                Subject = caseEntity.Subject,
                CustomerName = caseEntity.Customer?.Name ?? string.Empty,
                LastCommentId = comment.Id,
                LastCommentSnippet = comment.Body.Length > 140
                    ? comment.Body[..140] + "…"
                    : comment.Body,
                LastCommentAtUtc = comment.CreatedAtUtc,
                LastCommentAuthor = comment.AuthorUser?.FullName
                    ?? comment.AuthorCustomer?.Name
                    ?? "Unknown",
                Unread = unread,
                UnreadCount = unreadCount,
            });
        }

        // Most-recent activity first.
        result.Sort((a, b) => b.LastCommentAtUtc.CompareTo(a.LastCommentAtUtc));
        return result;
    }

    /// <inheritdoc />
    public async Task MarkConversationReadAsync(int caseId, string agentUserId)
    {
        var existing = await _readStates.Query()
            .FirstOrDefaultAsync(r => r.CaseId == caseId && r.AgentUserId == agentUserId);

        if (existing is null)
        {
            await _readStates.AddAsync(new ConversationReadState
            {
                CaseId = caseId,
                AgentUserId = agentUserId,
                LastViewedUtc = DateTime.UtcNow,
            });
        }
        else
        {
            existing.LastViewedUtc = DateTime.UtcNow;
            _readStates.Update(existing);
        }

        await _readStates.SaveChangesAsync();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ConversationSummaryDto>> GetAllConversationsAsync(string viewerUserId)
    {
        // All cases that have at least one comment, regardless of assignment.
        var caseIdsWithComments = await _comments.Query()
            .Select(cm => cm.CaseId)
            .Distinct()
            .ToListAsync();

        if (caseIdsWithComments.Count == 0)
        {
            return Array.Empty<ConversationSummaryDto>();
        }

        var latestComments = await _comments.Query()
            .Include(cm => cm.AuthorUser)
            .Include(cm => cm.AuthorCustomer)
            .Where(cm => caseIdsWithComments.Contains(cm.CaseId))
            .GroupBy(cm => cm.CaseId)
            .Select(g => g.OrderByDescending(cm => cm.CreatedAtUtc).First())
            .ToListAsync();

        var readStates = await _readStates.Query()
            .Where(r => r.AgentUserId == viewerUserId && caseIdsWithComments.Contains(r.CaseId))
            .ToDictionaryAsync(r => r.CaseId, r => r.LastViewedUtc);

        // Latest NON-SELF comment per case — used for unread detection so
        // a user's own reply never makes the conversation appear unread to them.
        var latestNonSelfComments = await _comments.Query()
            .Where(cm => caseIdsWithComments.Contains(cm.CaseId) && cm.AuthorUserId != viewerUserId)
            .GroupBy(cm => cm.CaseId)
            .Select(g => new { CaseId = g.Key, LatestAt = g.Max(cm => cm.CreatedAtUtc) })
            .ToDictionaryAsync(x => x.CaseId, x => x.LatestAt);

        // All non-self comments with timestamps — used to count the actual
        // number of unread messages per conversation (vs the boolean flag).
        var allNonSelfComments = await _comments.Query()
            .Where(cm => caseIdsWithComments.Contains(cm.CaseId) && cm.AuthorUserId != viewerUserId)
            .Select(cm => new { cm.CaseId, cm.CreatedAtUtc })
            .ToListAsync();

        var nonSelfByCase = allNonSelfComments
            .GroupBy(cm => cm.CaseId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Batch-load all cases in one query to avoid N+1 per-case lookups.
        var caseEntities = await _cases.Query()
            .Include(c => c.Customer)
            .Include(c => c.AssignedToUser)
            .Where(c => caseIdsWithComments.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id);

        var result = new List<ConversationSummaryDto>();
        foreach (var comment in latestComments)
        {
            if (!caseEntities.TryGetValue(comment.CaseId, out var caseEntity))
            {
                continue;
            }

            var lastViewed = readStates.TryGetValue(comment.CaseId, out var v) ? v : DateTime.MinValue;
            var latestNonSelfAt = latestNonSelfComments.TryGetValue(comment.CaseId, out var t) ? t : DateTime.MinValue;
            var unread = latestNonSelfAt > lastViewed;

            var unreadCount = 0;
            if (nonSelfByCase.TryGetValue(comment.CaseId, out var caseComments))
            {
                unreadCount = caseComments.Count(cm => cm.CreatedAtUtc > lastViewed);
            }

            result.Add(new ConversationSummaryDto
            {
                CaseId = comment.CaseId,
                CaseDisplayId = caseEntity.CaseDisplayId,
                Subject = caseEntity.Subject,
                CustomerName = caseEntity.Customer?.Name ?? string.Empty,
                AssignedAgentName = caseEntity.AssignedToUser?.FullName,
                LastCommentId = comment.Id,
                LastCommentSnippet = comment.Body.Length > 140
                    ? comment.Body[..140] + "…"
                    : comment.Body,
                LastCommentAtUtc = comment.CreatedAtUtc,
                LastCommentAuthor = comment.AuthorUser?.FullName
                    ?? comment.AuthorCustomer?.Name
                    ?? "Unknown",
                Unread = unread,
                UnreadCount = unreadCount,
            });
        }

        result.Sort((a, b) => b.LastCommentAtUtc.CompareTo(a.LastCommentAtUtc));
        return result;
    }

    internal static CaseDto ToDto(Case c) => new()
    {
        Id = c.Id,
        CaseDisplayId = c.CaseDisplayId,
        Subject = c.Subject,
        Description = c.Description,
        Status = c.Status,
        Priority = c.Priority,
        PriorityAutoSuggested = c.PriorityAutoSuggested,
        PriorityReason = c.PriorityReason,
        CustomerId = c.CustomerId,
        CustomerName = c.Customer != null ? c.Customer.Name : string.Empty,
        CategoryId = c.CategoryId,
        CategoryName = c.Category != null ? c.Category.Name : string.Empty,
        AssignedToUserId = c.AssignedToUserId,
        AssignedToUserName = c.AssignedToUser != null ? c.AssignedToUser.FullName : null,
        AssignedAtUtc = c.AssignedAtUtc,
        CreatedAtUtc = c.CreatedAtUtc,
        UpdatedAtUtc = c.UpdatedAtUtc,
        FollowUpDueUtc = c.FollowUpDueUtc,
        DaysOverdue = OverduePolicy.NeedsFollowUp(c) ? OverduePolicy.DaysOverdue(c) : null,
        CommentCount = c.Comments?.Count ?? 0,
        // Soft-delete / recycle metadata so the UI can render deleted-mode
        // detail and the restore-gating hint without a second call.
        IsDeleted = c.IsDeleted,
        DeletedAtUtc = c.DeletedAtUtc,
        DeletedById = c.DeletedById,
        Purged = c.Purged,
        CustomerIsDeleted = c.Customer?.IsDeleted ?? false,
    };
}
