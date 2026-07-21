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
        IPriorityPredictor predictor,
        INotificationService notifications)
    {
        _cases = cases;
        _customers = customers;
        _categories = categories;
        _comments = comments;
        _readStates = readStates;
        _predictor = predictor;
        _notifications = notifications;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CaseDto>> GetAllAsync(
        CaseStatus? status, Priority? priority, int? categoryId, DateTime? from, DateTime? to, bool overdue = false, string? assignedToUserId = null, string? callerRole = null, string? callerUserId = null)
    {
        IQueryable<Case> q = _cases.Query()
            .Include(c => c.Customer)
            .Include(c => c.Category);

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
        var c = await _cases.Query()
            .Include(c => c.Customer)
            .Include(c => c.Category)
            .Include(c => c.AssignedToUser)
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
        return ToDto(caseEntity);
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(int id, UpdateCaseDto dto, string? callerRole = null, string? callerUserId = null)
    {
        var caseEntity = await _cases.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Case {id} not found.");

        var isAgent = string.Equals(callerRole, nameof(UserRole.Agent), StringComparison.OrdinalIgnoreCase);

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
            }
            else if (dto.AssignedToUserId is not null)
            {
                caseEntity.AssignedToUserId = dto.AssignedToUserId;
            }
        }

        caseEntity.PriorityAutoSuggested = false; // manual override
        caseEntity.UpdatedAtUtc = DateTime.UtcNow;
        _cases.Update(caseEntity);
        await _cases.SaveChangesAsync();

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
    public async Task DeleteAsync(int id)
    {
        var caseEntity = await _cases.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Case {id} not found.");
        _cases.Remove(caseEntity);
        await _cases.SaveChangesAsync();
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

        var result = new List<ConversationSummaryDto>();
        foreach (var comment in latestComments)
        {
            var caseEntity = await _cases.Query()
                .Include(c => c.Customer)
                .FirstOrDefaultAsync(c => c.Id == comment.CaseId);
            if (caseEntity is null)
            {
                continue;
            }

            var lastViewed = readStates.TryGetValue(comment.CaseId, out var v) ? v : DateTime.MinValue;
            var unread = comment.CreatedAtUtc > lastViewed;

            result.Add(new ConversationSummaryDto
            {
                CaseId = comment.CaseId,
                Subject = caseEntity.Subject,
                CustomerName = caseEntity.Customer?.Name ?? string.Empty,
                LastCommentSnippet = comment.Body.Length > 140
                    ? comment.Body[..140] + "…"
                    : comment.Body,
                LastCommentAtUtc = comment.CreatedAtUtc,
                LastCommentAuthor = comment.AuthorUser?.FullName
                    ?? comment.AuthorCustomer?.Name
                    ?? "Unknown",
                Unread = unread,
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

    internal static CaseDto ToDto(Case c) => new()
    {
        Id = c.Id,
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
        CreatedAtUtc = c.CreatedAtUtc,
        UpdatedAtUtc = c.UpdatedAtUtc,
        FollowUpDueUtc = c.FollowUpDueUtc,
        DaysOverdue = OverduePolicy.NeedsFollowUp(c) ? OverduePolicy.DaysOverdue(c) : null,
    };
}
