using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;
using CustomerService.ML;
using Microsoft.EntityFrameworkCore;

namespace CustomerService.Application.Services;

/// <summary>
/// Implements <see cref="ICaseService"/>. On creation, when no priority is
/// supplied, the ML predictor suggests one (flagged as auto-suggested).
/// </summary>
public class CaseService : ICaseService
{
    private readonly IRepository<Case> _cases;
    private readonly IRepository<Customer> _customers;
    private readonly IRepository<Category> _categories;
    private readonly IPriorityPredictor _predictor;

    /// <summary>Initializes a new <see cref="CaseService"/>.</summary>
    /// <param name="cases">Case repository.</param>
    /// <param name="customers">Customer repository.</param>
    /// <param name="categories">Category repository.</param>
    /// <param name="predictor">Priority predictor (ML or rule-based fallback).</param>
    public CaseService(
        IRepository<Case> cases,
        IRepository<Customer> customers,
        IRepository<Category> categories,
        IPriorityPredictor predictor)
    {
        _cases = cases;
        _customers = customers;
        _categories = categories;
        _predictor = predictor;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CaseDto>> GetAllAsync(
        CaseStatus? status, Priority? priority, int? categoryId, DateTime? from, DateTime? to)
    {
        var q = _cases.Query();
        if (status.HasValue) q = q.Where(c => c.Status == status.Value);
        if (priority.HasValue) q = q.Where(c => c.Priority == priority.Value);
        if (categoryId.HasValue) q = q.Where(c => c.CategoryId == categoryId.Value);
        if (from.HasValue) q = q.Where(c => c.CreatedAtUtc >= from.Value);
        if (to.HasValue) q = q.Where(c => c.CreatedAtUtc <= to.Value);

        return await q.OrderByDescending(c => c.CreatedAtUtc)
            .Select(c => ToDto(c))
            .ToListAsync();
    }

    /// <inheritdoc/>
    public async Task<CaseDto?> GetByIdAsync(int id)
    {
        var c = await _cases.Query().FirstOrDefaultAsync(x => x.Id == id);
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
        var hasKeyword = RuleBasedPriorityPredictor.ContainsComplaintKeyword(dto.Description);

        var priority = dto.Priority
            ?? _predictor.Predict(new PriorityFeatures
            {
                CategoryId = dto.CategoryId,
                PriorCaseCount = priorCaseCount,
                DaysSinceLastContact = daysSince,
                HasComplaintKeyword = hasKeyword,
            });

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
            LastContactUtc = dto.LastContactUtc,
            CreatedAtUtc = DateTime.UtcNow,
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
        caseEntity.Subject = dto.Subject;
        caseEntity.Description = dto.Description;
        caseEntity.Status = dto.Status;
        caseEntity.Priority = dto.Priority;
        caseEntity.CategoryId = dto.CategoryId;
        caseEntity.AssignedToUserId = dto.AssignedToUserId;
        caseEntity.PriorityAutoSuggested = false; // manual override
        caseEntity.UpdatedAtUtc = DateTime.UtcNow;
        _cases.Update(caseEntity);
        await _cases.SaveChangesAsync();
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
        CustomerId = c.CustomerId,
        CustomerName = c.Customer != null ? c.Customer.Name : string.Empty,
        CategoryId = c.CategoryId,
        CategoryName = c.Category != null ? c.Category.Name : string.Empty,
        AssignedToUserId = c.AssignedToUserId,
        AssignedToUserName = c.AssignedToUser != null ? c.AssignedToUser.FullName : null,
        CreatedAtUtc = c.CreatedAtUtc,
        UpdatedAtUtc = c.UpdatedAtUtc,
    };
}
