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
    private readonly IPriorityPredictor _predictor;
    private readonly INotificationService _notifications;

    /// <summary>Initializes a new <see cref="CaseService"/>.</summary>
    /// <param name="cases">Case repository.</param>
    /// <param name="customers">Customer repository.</param>
    /// <param name="categories">Category repository.</param>
    /// <param name="predictor">Priority predictor (ML or rule-based fallback).</param>
    /// <param name="notifications">Notification service (resolved/customer email).</param>
    public CaseService(
        IRepository<Case> cases,
        IRepository<Customer> customers,
        IRepository<Category> categories,
        IPriorityPredictor predictor,
        INotificationService notifications)
    {
        _cases = cases;
        _customers = customers;
        _categories = categories;
        _predictor = predictor;
        _notifications = notifications;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CaseDto>> GetAllAsync(
        CaseStatus? status, Priority? priority, int? categoryId, DateTime? from, DateTime? to, bool overdue = false)
    {
        IQueryable<Case> q = _cases.Query()
            .Include(c => c.Customer)
            .Include(c => c.Category);
        if (status.HasValue) q = q.Where(c => c.Status == status.Value);
        if (priority.HasValue) q = q.Where(c => c.Priority == priority.Value);
        if (categoryId.HasValue) q = q.Where(c => c.CategoryId == categoryId.Value);
        if (from.HasValue) q = q.Where(c => c.CreatedAtUtc >= from.Value);
        if (to.HasValue) q = q.Where(c => c.CreatedAtUtc <= to.Value);
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
    public async Task<CaseDto?> GetByIdAsync(int id)
    {
        var c = await _cases.Query()
            .Include(c => c.Customer)
            .Include(c => c.Category)
            .Include(c => c.AssignedToUser)
            .FirstOrDefaultAsync(x => x.Id == id);
        return c is null ? null : ToDto(c);
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
    public async Task UpdateAsync(int id, UpdateCaseDto dto)
    {
        var caseEntity = await _cases.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Case {id} not found.");

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
        if (dto.AssignedToUserId == UpdateCaseDto.UnassignSentinel)
        {
            caseEntity.AssignedToUserId = null;
        }
        else if (dto.AssignedToUserId is not null)
        {
            caseEntity.AssignedToUserId = dto.AssignedToUserId;
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

    private static CaseDto ToDto(Case c) => new()
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
