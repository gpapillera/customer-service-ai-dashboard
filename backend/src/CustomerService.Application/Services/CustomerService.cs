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
    private readonly IRepository<Notification> _notifications;
    private readonly ICustomerDisplayIdGenerator _displayIdGenerator;

    /// <summary>Initializes a new <see cref="CustomerService"/>.</summary>
    /// <param name="customers">Customer repository.</param>
    /// <param name="cases">Case repository (for counts).</param>
    /// <param name="notifications">Notification repository (account + case emails for activity).</param>
    /// <param name="displayIdGenerator">Monotonic sequence for customer display IDs (C-NNNNN).</param>
    public CustomerService(
        IRepository<Customer> customers,
        IRepository<Case> cases,
        IRepository<Notification> notifications,
        ICustomerDisplayIdGenerator displayIdGenerator)
    {
        _customers = customers;
        _cases = cases;
        _notifications = notifications;
        _displayIdGenerator = displayIdGenerator;
    }

    /// <summary>
    /// Scans all cases (and their call logs, comments, notifications) for a customer
    /// and returns the most recent activity timestamp, a human-readable description,
    /// and the id of the case that produced the activity (so the UI can deep-link
    /// from the customer card to the right case's history even when a customer has
    /// more than one case).
    ///
    /// Account-level events (invite / password-reset emails and account activation)
    /// are folded in too, because a customer with no cases can still have real
    /// recent activity — without this the customer card footer shows only "Since …".
    /// </summary>
    private static (DateTime? atUtc, string? description, int? caseId) ComputeLastActivity(
        Customer c, IReadOnlyList<Notification> accountNotifications)
    {
        DateTime? latest = null;
        string? desc = null;
        int? caseId = null;

        if (c.Cases is not null)
        {
            foreach (var cs in c.Cases)
            {
                // Case creation
                if (cs.CreatedAtUtc > (latest ?? DateTime.MinValue))
                {
                    latest = cs.CreatedAtUtc;
                    desc = $"Opened case #{cs.Id}";
                    caseId = cs.Id;
                }

                // Case update (status change)
                if (cs.UpdatedAtUtc.HasValue && cs.UpdatedAtUtc > (latest ?? DateTime.MinValue))
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
                if (cs.ResolvedAtUtc.HasValue && cs.ResolvedAtUtc > (latest ?? DateTime.MinValue))
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
                        if (log.CreatedAtUtc > (latest ?? DateTime.MinValue))
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
                        if (comment.CreatedAtUtc > (latest ?? DateTime.MinValue))
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
                        if (n.CreatedAtUtc > (latest ?? DateTime.MinValue))
                        {
                            latest = n.CreatedAtUtc;
                            desc = "Sent email";
                            caseId = cs.Id;
                        }
                    }
                }
            }
        }

        // Account-level emails: invites, password resets, and any manual/email-channel
        // notification addressed to this customer (Recipient == customer.Email, CaseId null).
        foreach (var n in accountNotifications)
        {
            if (n.CreatedAtUtc > (latest ?? DateTime.MinValue))
            {
                latest = n.CreatedAtUtc;
                desc = n.Type switch
                {
                    NotificationType.CustomerInvite => "Invite sent",
                    NotificationType.CustomerPasswordReset => "Password reset sent",
                    NotificationType.AdminManual => "Email sent",
                    _ => "Email sent",
                };
                caseId = n.CaseId; // null for account-only emails
            }
        }

        // Account activation.
        if (c.Account?.ActivatedAtUtc is { } activated && activated > (latest ?? DateTime.MinValue))
        {
            latest = activated;
            desc = "Account activated";
            caseId = null;
        }

        return (latest, desc, caseId);
    }

    /// <summary>
    /// Builds the merged case-level activity timeline for a customer (case
    /// creation, status changes, resolution, call logs, comments, case emails).
    /// Account-level events are added separately by <see cref="GetCustomerActivityAsync"/>.
    /// </summary>
    private static List<CustomerActivityItemDto> BuildCaseActivityItems(Customer c)
    {
        var items = new List<CustomerActivityItemDto>();
        if (c.Cases is null) return items;

        foreach (var cs in c.Cases)
        {
            items.Add(new CustomerActivityItemDto
            {
                Id = cs.Id,
                Kind = "opened",
                Label = "Opened",
                Detail = "Case created",
                AtUtc = cs.CreatedAtUtc,
                CaseId = cs.Id,
            });

            if (cs.UpdatedAtUtc.HasValue)
            {
                var statusLabel = cs.Status == CaseStatus.New ? cs.Status.ToString() : $"moved to {cs.Status}";
                items.Add(new CustomerActivityItemDto
                {
                    Id = cs.Id * 100 + 1,
                    Kind = "updated",
                    Label = "Updated",
                    Detail = $"Status {statusLabel}",
                    AtUtc = cs.UpdatedAtUtc.Value,
                    CaseId = cs.Id,
                });
            }

            if (cs.ResolvedAtUtc.HasValue)
            {
                items.Add(new CustomerActivityItemDto
                {
                    Id = cs.Id * 100 + 2,
                    Kind = cs.Status == CaseStatus.Closed ? "resolved" : "resolved",
                    Label = cs.Status == CaseStatus.Closed ? "Closed" : "Resolved",
                    Detail = cs.Status == CaseStatus.Closed ? $"Case #{cs.Id} closed" : $"Case #{cs.Id} resolved",
                    AtUtc = cs.ResolvedAtUtc.Value,
                    CaseId = cs.Id,
                });
            }

            if (cs.CallLogs is not null)
            {
                foreach (var log in cs.CallLogs)
                {
                    items.Add(new CustomerActivityItemDto
                    {
                        Id = log.Id,
                        Kind = "log",
                        Label = log.Direction.ToString(),
                        Detail = log.Notes,
                        AtUtc = log.CreatedAtUtc,
                        CaseId = cs.Id,
                    });
                }
            }

            if (cs.Comments is not null)
            {
                foreach (var comment in cs.Comments)
                {
                    var isStaff = comment.AuthorUserId != null;
                    var who = isStaff ? "Staff" : "Customer";
                    var what = isStaff ? "Staff comment" : "Customer message";
                    items.Add(new CustomerActivityItemDto
                    {
                        Id = comment.Id,
                        Kind = "comment",
                        Label = what,
                        Detail = comment.Body,
                        AtUtc = comment.CreatedAtUtc,
                        CaseId = cs.Id,
                        Who = who,
                    });
                }
            }

            if (cs.Notifications is not null)
            {
                foreach (var n in cs.Notifications)
                {
                    if (n.Channel != NotificationChannel.Email && n.Type != NotificationType.AdminManual)
                        continue;
                    items.Add(new CustomerActivityItemDto
                    {
                        Id = n.Id,
                        Kind = "email",
                        Label = "Email sent",
                        Detail = n.Title ?? n.Message,
                        AtUtc = n.CreatedAtUtc,
                        CaseId = cs.Id,
                        Who = n.Recipient,
                    });
                }
            }
        }

        return items;
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
            q = hasAccount.Value
                ? q.Where(c => c.Account != null)
                : q.Where(c => c.Account == null);
        }

        // Sorting (Phase 24f)
        bool desc = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(sortBy, "activity", StringComparison.OrdinalIgnoreCase))
        {
            q = desc
                ? q.OrderByDescending(c => c.Cases!.Max(cs => (DateTime?)cs.CreatedAtUtc) ?? c.CreatedAtUtc)
                : q.OrderBy(c => c.Cases!.Max(cs => (DateTime?)cs.CreatedAtUtc) ?? c.CreatedAtUtc);
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

        // Batch-load all relevant notifications once (avoids per-customer N+1):
        // any notification addressed to a loaded customer's email, or tied to a
        // loaded case. Case notifications are already in the graph; this also
        // pulls account-only emails (CaseId null, Recipient == customer.Email).
        var custEmails = loaded.Select(c => c.Email).ToHashSet();
        var caseIds = loaded.SelectMany(c => c.Cases ?? new List<Case>()).Select(cs => cs.Id).ToHashSet();
        var allNotes = await _notifications.Query()
            .Where(n => (n.Recipient != null && custEmails.Contains(n.Recipient))
                     || (n.CaseId != null && caseIds.Contains(n.CaseId.Value)))
            .ToListAsync();

        var result = loaded.Select(c =>
        {
            var acctNotes = allNotes.Where(n =>
                n.Recipient == c.Email ||
                (n.CaseId != null && (c.Cases ?? new List<Case>()).Any(cs => cs.Id == n.CaseId.Value)))
                .ToList();
            return ToDto(c, acctNotes);
        }).ToList();

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

        var caseIds = (c.Cases ?? new List<Case>()).Select(cs => cs.Id).ToHashSet();
        var acctNotes = await _notifications.Query()
            .Where(n => n.Recipient == c.Email ||
                        (n.CaseId != null && caseIds.Contains(n.CaseId.Value)))
            .ToListAsync();

        return ToDto(c, acctNotes);
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
            q = hasAccount.Value
                ? q.Where(c => c.Account != null)
                : q.Where(c => c.Account == null);
        }

        // Sorting (Phase 24f)
        bool desc = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(sortBy, "activity", StringComparison.OrdinalIgnoreCase))
        {
            q = desc
                ? q.OrderByDescending(c => c.Cases!.Max(cs => (DateTime?)cs.CreatedAtUtc) ?? c.CreatedAtUtc)
                : q.OrderBy(c => c.Cases!.Max(cs => (DateTime?)cs.CreatedAtUtc) ?? c.CreatedAtUtc);
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

        // Batch-load all relevant notifications once (avoids per-customer N+1).
        var custEmails = loaded.Select(c => c.Email).ToHashSet();
        var caseIds = loaded.SelectMany(c => c.Cases ?? new List<Case>()).Select(cs => cs.Id).ToHashSet();
        var allNotes = await _notifications.Query()
            .Where(n => (n.Recipient != null && custEmails.Contains(n.Recipient))
                     || (n.CaseId != null && caseIds.Contains(n.CaseId.Value)))
            .ToListAsync();

        var result = loaded.Select(c =>
        {
            var acctNotes = allNotes.Where(n =>
                n.Recipient == c.Email ||
                (n.CaseId != null && (c.Cases ?? new List<Case>()).Any(cs => cs.Id == n.CaseId.Value)))
                .ToList();
            return ToDto(c, acctNotes);
        }).ToList();

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
        // Assign a human-readable display ID from the shared monotonic sequence.
        // Done up front (not derived from the row Id) so the value is unique even
        // if a customer is later deleted — the generator never reuses a number.
        customer.CustomerDisplayId = _displayIdGenerator.Next();
        await _customers.SaveChangesAsync();
        return ToDto(customer, new List<Notification>());
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

    /// <summary>
    /// Returns every email sent to this customer: account-level invites/resets/
    /// manual emails (Recipient == customer.Email, CaseId null) plus any case
    /// emails (CaseId belongs to this customer). Newest first.
    /// </summary>
    public async Task<IReadOnlyList<NotificationDto>> GetCustomerEmailsAsync(int customerId, string? callerRole = null, string? callerUserId = null)
    {
        var c = await _customers.GetByIdAsync(customerId)
            ?? throw new KeyNotFoundException($"Customer {customerId} not found.");

        // Agent scoping (Phase 6): must share a case with this customer.
        var isAgent = string.Equals(callerRole, nameof(UserRole.Agent), StringComparison.OrdinalIgnoreCase);
        if (isAgent && !string.IsNullOrEmpty(callerUserId))
        {
            var sharesCase = await _cases.Query()
                .AnyAsync(x => x.CustomerId == customerId && x.AssignedToUserId == callerUserId);
            if (!sharesCase)
            {
                throw new ForbiddenException("You can only view customers you share a case with.");
            }
        }

        var caseIds = (c.Cases ?? new List<Case>()).Select(cs => cs.Id).ToHashSet();
        var notes = await _notifications.Query()
            .Where(n => n.Recipient == c.Email ||
                        (n.CaseId != null && caseIds.Contains(n.CaseId.Value)))
            .OrderByDescending(n => n.CreatedAtUtc)
            .ToListAsync();

        return notes.Select(NotificationDto.FromEntity).ToList();
    }

    /// <summary>
    /// Returns the merged case + account activity timeline for a customer,
    /// newest first. Account events (invite / reset / activation) are included
    /// even when the customer has no cases.
    /// </summary>
    public async Task<IReadOnlyList<CustomerActivityItemDto>> GetCustomerActivityAsync(int customerId, string? callerRole = null, string? callerUserId = null)
    {
        var c = await _customers.Query()
            .Include(x => x.Account)
            .Include(x => x.Cases).ThenInclude(cs => cs.CallLogs)
            .Include(x => x.Cases).ThenInclude(cs => cs.Comments)
            .Include(x => x.Cases).ThenInclude(cs => cs.Notifications)
            .FirstOrDefaultAsync(x => x.Id == customerId)
            ?? throw new KeyNotFoundException($"Customer {customerId} not found.");

        // Agent scoping (Phase 6): must share a case with this customer.
        var isAgent = string.Equals(callerRole, nameof(UserRole.Agent), StringComparison.OrdinalIgnoreCase);
        if (isAgent && !string.IsNullOrEmpty(callerUserId))
        {
            var sharesCase = await _cases.Query()
                .AnyAsync(x => x.CustomerId == customerId && x.AssignedToUserId == callerUserId);
            if (!sharesCase)
            {
                throw new ForbiddenException("You can only view customers you share a case with.");
            }
        }

        var items = BuildCaseActivityItems(c);

        // Account-level emails (invite / reset / manual; CaseId null + Recipient == email).
        // Materialize the case-id set so the query translates (n.CaseId is nullable
        // and can't be compared to the in-graph Cases collection client-side).
        var caseIds = (c.Cases ?? new List<Case>()).Select(cs => cs.Id).ToHashSet();
        if (c.Cases is not null && caseIds.Count > 0)
        {
            var accountNotes = await _notifications.Query()
                .Where(n => n.Recipient == c.Email && (n.CaseId == null || !caseIds.Contains(n.CaseId.Value)))
                .ToListAsync();
            foreach (var n in accountNotes)
            {
                items.Add(new CustomerActivityItemDto
                {
                    Id = n.Id,
                    Kind = n.Type switch
                    {
                        NotificationType.CustomerInvite => "account_invite",
                        NotificationType.CustomerPasswordReset => "account_reset",
                        _ => "email",
                    },
                    Label = n.Type switch
                    {
                        NotificationType.CustomerInvite => "Invite sent",
                        NotificationType.CustomerPasswordReset => "Password reset sent",
                        NotificationType.AdminManual => "Email sent",
                        _ => "Email sent",
                    },
                    Detail = n.Title ?? n.Message,
                    AtUtc = n.CreatedAtUtc,
                    CaseId = null,
                    Who = n.Recipient,
                });
            }
        }
        else
        {
            // No cases: every notification for this recipient is account-level.
            var accountNotes = await _notifications.Query()
                .Where(n => n.Recipient == c.Email)
                .ToListAsync();
            foreach (var n in accountNotes)
            {
                items.Add(new CustomerActivityItemDto
                {
                    Id = n.Id,
                    Kind = n.Type switch
                    {
                        NotificationType.CustomerInvite => "account_invite",
                        NotificationType.CustomerPasswordReset => "account_reset",
                        _ => "email",
                    },
                    Label = n.Type switch
                    {
                        NotificationType.CustomerInvite => "Invite sent",
                        NotificationType.CustomerPasswordReset => "Password reset sent",
                        NotificationType.AdminManual => "Email sent",
                        _ => "Email sent",
                    },
                    Detail = n.Title ?? n.Message,
                    AtUtc = n.CreatedAtUtc,
                    CaseId = null,
                    Who = n.Recipient,
                });
            }
        }

        // Account activation.
        if (c.Account?.ActivatedAtUtc is { } activated)
        {
            items.Add(new CustomerActivityItemDto
            {
                Id = -1,
                Kind = "account_activated",
                Label = "Account activated",
                Detail = "Portal account activated",
                AtUtc = activated,
                CaseId = null,
            });
        }

        return items
            .OrderByDescending(i => i.AtUtc)
            .ToList();
    }

    private static CustomerDto ToDto(Customer c, IReadOnlyList<Notification> accountNotifications)
    {
        var (lastActivityAt, lastActivityDesc, lastActivityCaseId) = ComputeLastActivity(c, accountNotifications);
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
