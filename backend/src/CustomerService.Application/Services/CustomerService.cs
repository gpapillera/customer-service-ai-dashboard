using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using CustomerService.Domain;
using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CustomerService.Application.Services;

/// <summary>
/// Implements <see cref="ICustomerService"/> using repositories.
/// </summary>
public class CustomerService : ICustomerService
{
    private readonly IRepository<Customer> _customers;
    private readonly IRepository<Case> _cases;

    /// <summary>Initializes a new <see cref="CustomerService"/>.</summary>
    /// <param name="customers">Customer repository.</param>
    /// <param name="cases">Case repository (for counts).</param>
    public CustomerService(IRepository<Customer> customers, IRepository<Case> cases)
    {
        _customers = customers;
        _cases = cases;
    }

    /// <summary>
    /// Scans all cases (and their call logs, comments, notifications) for a customer
    /// and returns the most recent activity timestamp, a human-readable description,
    /// and the id of the case that produced the activity (so the UI can deep-link
    /// from the customer card to the right case's history even when a customer has
    /// more than one case).
    /// </summary>
    private static (DateTime? atUtc, string? description, int? caseId) ComputeLastActivity(Customer c)
    {
        DateTime? latest = null;
        string? desc = null;
        int? caseId = null;

        if (c.Cases is null) return (null, null, null);

        foreach (var cs in c.Cases)
        {
            // Case creation
            if (cs.CreatedAtUtc > latest || latest is null)
            {
                latest = cs.CreatedAtUtc;
                desc = $"Opened case #{cs.Id}";
                caseId = cs.Id;
            }

            // Case update (status change)
            if (cs.UpdatedAtUtc.HasValue && (cs.UpdatedAtUtc > latest || latest is null))
            {
                latest = cs.UpdatedAtUtc.Value;
                desc = cs.Status switch
                {
                    CaseStatus.Resolved => $"Resolved case #{cs.Id}",
                    CaseStatus.Closed => $"Closed case #{cs.Id}",
                    _ => $"Updated case #{cs.Id}",
                };
                caseId = cs.Id;
            }

            // Resolution timestamp (separate from UpdatedAtUtc for resolved/closed)
            if (cs.ResolvedAtUtc.HasValue && (cs.ResolvedAtUtc > latest || latest is null))
            {
                latest = cs.ResolvedAtUtc.Value;
                desc = cs.Status switch
                {
                    CaseStatus.Closed => $"Closed case #{cs.Id}",
                    _ => $"Resolved case #{cs.Id}",
                };
                caseId = cs.Id;
            }

            // Call logs
            if (cs.CallLogs is not null)
            {
                foreach (var log in cs.CallLogs)
                {
                    if (log.CreatedAtUtc > latest || latest is null)
                    {
                        latest = log.CreatedAtUtc;
                        desc = "Updated call log";
                        caseId = cs.Id;
                    }
                }
            }

            // Comments
            if (cs.Comments is not null)
            {
                foreach (var comment in cs.Comments)
                {
                    if (comment.CreatedAtUtc > latest || latest is null)
                    {
                        latest = comment.CreatedAtUtc;
                        desc = comment.AuthorUserId != null ? "Messaged customer" : "Customer replied";
                        caseId = cs.Id;
                    }
                }
            }

            // Notifications — only count actual email sends (AdminManual or Email channel),
            // not internal in-app alerts (overdue reminders etc.).
            if (cs.Notifications is not null)
            {
                foreach (var n in cs.Notifications)
                {
                    if (n.Channel != NotificationChannel.Email && n.Type != NotificationType.AdminManual)
                        continue;
                    if (n.CreatedAtUtc > latest || latest is null)
                    {
                        latest = n.CreatedAtUtc;
                        desc = "Sent email";
                        caseId = cs.Id;
                    }
                }
            }
        }

        return (latest, desc, caseId);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CustomerDto>> GetAllAsync(string? callerRole = null, string? callerUserId = null,
        bool? hasAccount = null, string? sortBy = null, string? sortDirection = null)
    {
        var isAgent = string.Equals(callerRole, nameof(UserRole.Agent), StringComparison.OrdinalIgnoreCase);

        // SERVER-SIDE AGENT SCOPING (Phase 6). An Agent only sees customers who
        // have at least one case assigned to them (join/exists query, not
        // client-side filtering). Admin is unaffected.
        IQueryable<Customer> q = _customers.Query();

        if (isAgent && !string.IsNullOrEmpty(callerUserId))
        {
            var customerIds = await _cases.Query()
                .Where(c => c.AssignedToUserId == callerUserId)
                .Select(c => c.CustomerId)
                .Distinct()
                .ToListAsync();
            q = q.Where(c => customerIds.Contains(c.Id));
        }

        // Has-account filter (Phase 24f)
        if (hasAccount.HasValue)
        {
            if (hasAccount.Value)
                q = q.Where(c => c.Account != null);
            else
                q = q.Where(c => c.Account == null);
        }

        // Sorting (Phase 24f)
        bool desc = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(sortBy, "activity", StringComparison.OrdinalIgnoreCase))
        {
            q = desc
                ? q.OrderByDescending(c => c.Cases.Max(cs => (DateTime?)cs.CreatedAtUtc) ?? c.CreatedAtUtc)
                : q.OrderBy(c => c.Cases.Max(cs => (DateTime?)cs.CreatedAtUtc) ?? c.CreatedAtUtc);
        }
        else
        {
            q = desc
                ? q.OrderByDescending(c => c.Name)
                : q.OrderBy(c => c.Name);
        }

        // Load full entity graph for in-memory mapping (needed for ComputeLastActivity).
        var loaded = await q
            .Include(c => c.Account)
            .Include(c => c.Cases).ThenInclude(cs => cs.CallLogs)
            .Include(c => c.Cases).ThenInclude(cs => cs.Comments)
            .Include(c => c.Cases).ThenInclude(cs => cs.Notifications)
            .ToListAsync();

        var result = loaded.Select(c => ToDto(c)).ToList();

        // Re-sort by computed last-activity when sortBy=activity (in-memory after computing).
        if (string.Equals(sortBy, "activity", StringComparison.OrdinalIgnoreCase))
        {
            result = desc
                ? result.OrderByDescending(dto => dto.LastActivityAtUtc ?? dto.CreatedAtUtc).ToList()
                : result.OrderBy(dto => dto.LastActivityAtUtc ?? dto.CreatedAtUtc).ToList();
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<CustomerDto?> GetByIdAsync(int id, string? callerRole = null, string? callerUserId = null)
    {
        var c = await _customers.Query()
            .Include(x => x.Account)
            .Include(x => x.Cases).ThenInclude(cs => cs.CallLogs)
            .Include(x => x.Cases).ThenInclude(cs => cs.Comments)
            .Include(x => x.Cases).ThenInclude(cs => cs.Notifications)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return null;

        // AGENT SCOPING (Phase 6). An Agent may only open a customer they share
        // at least one case with. Admin is unaffected.
        var isAgent = string.Equals(callerRole, nameof(UserRole.Agent), StringComparison.OrdinalIgnoreCase);
        if (isAgent && !string.IsNullOrEmpty(callerUserId))
        {
            var sharesCase = await _cases.Query()
                .AnyAsync(x => x.CustomerId == id && x.AssignedToUserId == callerUserId);
            if (!sharesCase)
            {
                throw new ForbiddenException("You can only view customers you share a case with.");
            }
        }

        return ToDto(c);
    }

    /// <summary>
    /// Returns the case history for a customer, scoped for an Agent caller to
    /// only the cases assigned to them. Admin sees the full history. Used by
    /// the customer detail endpoint so an Agent never sees another agent's
    /// cases with the same customer.
    /// </summary>
    /// <param name="customerId">Customer id.</param>
    /// <param name="callerRole">Role of the calling user.</param>
    /// <param name="callerUserId">Id of the calling user (used to scope an Agent's view).</param>
    /// <returns>The customer's cases visible to the caller.</returns>
    public async Task<IReadOnlyList<CaseDto>> GetCustomerCaseHistoryAsync(int customerId, string? callerRole = null, string? callerUserId = null)
    {
        var isAgent = string.Equals(callerRole, nameof(UserRole.Agent), StringComparison.OrdinalIgnoreCase);
        IQueryable<Case> q = _cases.Query()
            .Include(c => c.Customer)
            .Include(c => c.Category)
            .Where(c => c.CustomerId == customerId);
        if (isAgent && !string.IsNullOrEmpty(callerUserId))
        {
            q = q.Where(c => c.AssignedToUserId == callerUserId);
        }
        return await q.OrderByDescending(c => c.CreatedAtUtc)
            .Select(c => CaseService.ToDto(c))
            .ToListAsync();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CustomerDto>> SearchAsync(string? term, string? callerRole = null, string? callerUserId = null,
        bool? hasAccount = null, string? sortBy = null, string? sortDirection = null)
    {
        var isAgent = string.Equals(callerRole, nameof(UserRole.Agent), StringComparison.OrdinalIgnoreCase);

        // SERVER-SIDE AGENT SCOPING (Phase 6): same rule as GetAllAsync — an
        // Agent only searches within customers who share a case with them.
        IQueryable<Customer> q = _customers.Query();
        if (isAgent && !string.IsNullOrEmpty(callerUserId))
        {
            var customerIds = await _cases.Query()
                .Where(c => c.AssignedToUserId == callerUserId)
                .Select(c => c.CustomerId)
                .Distinct()
                .ToListAsync();
            q = q.Where(c => customerIds.Contains(c.Id));
        }

        if (!string.IsNullOrWhiteSpace(term))
        {
            term = term.Trim().ToLower();
            q = q.Where(c =>
                c.Name.ToLower().Contains(term) ||
                c.Email.ToLower().Contains(term) ||
                (c.Phone != null && c.Phone.Contains(term)));
        }

        // Has-account filter (Phase 24f)
        if (hasAccount.HasValue)
        {
            if (hasAccount.Value)
                q = q.Where(c => c.Account != null);
            else
                q = q.Where(c => c.Account == null);
        }

        // Sorting (Phase 24f)
        bool desc = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(sortBy, "activity", StringComparison.OrdinalIgnoreCase))
        {
            q = desc
                ? q.OrderByDescending(c => c.Cases.Max(cs => (DateTime?)cs.CreatedAtUtc) ?? c.CreatedAtUtc)
                : q.OrderBy(c => c.Cases.Max(cs => (DateTime?)cs.CreatedAtUtc) ?? c.CreatedAtUtc);
        }
        else
        {
            q = desc
                ? q.OrderByDescending(c => c.Name)
                : q.OrderBy(c => c.Name);
        }

        // Load full entity graph for in-memory mapping (needed for ComputeLastActivity).
        var loaded = await q
            .Include(c => c.Account)
            .Include(c => c.Cases).ThenInclude(cs => cs.CallLogs)
            .Include(c => c.Cases).ThenInclude(cs => cs.Comments)
            .Include(c => c.Cases).ThenInclude(cs => cs.Notifications)
            .ToListAsync();

        var result = loaded.Select(c => ToDto(c)).ToList();

        // Re-sort by computed last-activity when sortBy=activity (in-memory after computing).
        if (string.Equals(sortBy, "activity", StringComparison.OrdinalIgnoreCase))
        {
            result = desc
                ? result.OrderByDescending(dto => dto.LastActivityAtUtc ?? dto.CreatedAtUtc).ToList()
                : result.OrderBy(dto => dto.LastActivityAtUtc ?? dto.CreatedAtUtc).ToList();
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<CustomerDto> CreateAsync(CreateCustomerDto dto)
    {
        var customer = new Customer
        {
            Name = dto.Name,
            Email = NormalizeEmail(dto.Email),
            Phone = NormalizePhone(dto.Phone),
            Company = dto.Company,
            Address = dto.Address,
        };
        await _customers.AddAsync(customer);
        await _customers.SaveChangesAsync();
        // Generate a human-readable display ID after the entity is saved (Id is now set).
        customer.CustomerDisplayId = $"C-{customer.Id:D5}";
        _customers.Update(customer);
        await _customers.SaveChangesAsync();
        return ToDto(customer);
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(UpdateCustomerDto dto)
    {
        var customer = await _customers.GetByIdAsync(dto.Id)
            ?? throw new KeyNotFoundException($"Customer {dto.Id} not found.");
        customer.Name = dto.Name;
        customer.Email = NormalizeEmail(dto.Email);
        customer.Phone = NormalizePhone(dto.Phone);
        customer.Company = dto.Company;
        customer.Address = dto.Address;
        _customers.Update(customer);
        await _customers.SaveChangesAsync();
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(int id, string? callerRole = null)
    {
        // Only Admin may delete customers (mirrors CaseService.DeleteAsync).
        if (!string.Equals(callerRole, nameof(UserRole.Admin), StringComparison.OrdinalIgnoreCase))
            throw new ForbiddenException("Only admins can delete customers.");

        // Load the customer with the full graph so we can nullify
        // CaseComment.AuthorCustomerId before removal (that FK uses NoAction,
        // so EF cannot auto-cascade and would throw on DELETE).
        var customer = await _customers.Query()
            .Include(c => c.Cases).ThenInclude(cs => cs.Comments)
            .FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new KeyNotFoundException($"Customer {id} not found.");

        // Nullify CaseComment.AuthorCustomerId on every comment authored by
        // this customer — comments survive on their cases but lose the
        // authorship link (acceptable since the customer is being deleted).
        if (customer.Cases is not null)
        {
            foreach (var cs in customer.Cases)
            {
                if (cs.Comments is null) continue;
                foreach (var comment in cs.Comments)
                {
                    if (comment.AuthorCustomerId == id)
                        comment.AuthorCustomerId = null;
                }
            }
        }

        _customers.Remove(customer);
        await _customers.SaveChangesAsync();
    }

    private static CustomerDto ToDto(Customer c)
    {
        var (lastActivityAt, lastActivityDesc, lastActivityCaseId) = ComputeLastActivity(c);
        return new()
        {
            Id = c.Id,
            CustomerDisplayId = c.CustomerDisplayId,
            Name = c.Name,
            Email = c.Email,
            Phone = c.Phone,
            Company = c.Company,
            Address = c.Address,
            CaseCount = c.Cases?.Count ?? 0,
            ActiveCaseCount = c.Cases?.Count(cs => cs.Status != CaseStatus.Resolved && cs.Status != CaseStatus.Closed) ?? 0,
            ActiveCases = (c.Cases?.Where(cs => cs.Status != CaseStatus.Resolved && cs.Status != CaseStatus.Closed)
                .Select(cs => new ActiveCaseInfoDto
                {
                    Subject = cs.Subject,
                    Status = cs.Status,
                })
                .ToList()) ?? new List<ActiveCaseInfoDto>(),
            CreatedAtUtc = c.CreatedAtUtc,
            LastActivityAtUtc = lastActivityAt,
            LastActivityDescription = lastActivityDesc,
            LastActivityCaseId = lastActivityCaseId,
            HasAccount = c.Account != null,
            AccountActive = c.Account != null && c.Account.IsActive,
        };
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLower();

    private static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return phone!.Trim().StartsWith("+") ? "+" + digits : digits;
    }
}
