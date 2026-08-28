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
    private readonly IRepository<CustomerActivity> _activities;
    private readonly ICustomerDisplayIdGenerator _displayIdGenerator;
    private readonly IViewEventService _viewEvents;
    private readonly ILiveUpdateHub _events;

    /// <summary>Initializes a new <see cref="CustomerService"/>.</summary>
    /// <param name="customers">Customer repository.</param>
    /// <param name="cases">Case repository (for counts).</param>
    /// <param name="notifications">Notification repository (account + case emails for activity).</param>
    /// <param name="activities">Customer-activity audit repository (profile edits).</param>
    /// <param name="displayIdGenerator">Monotonic sequence for customer display IDs (C-NNNNN).</param>
    /// <param name="viewEvents">Viewed/opened audit service (Case + Customer detail page reads).</param>
    public CustomerService(
        IRepository<Customer> customers,
        IRepository<Case> cases,
        IRepository<Notification> notifications,
        IRepository<CustomerActivity> activities,
        ICustomerDisplayIdGenerator displayIdGenerator,
        IViewEventService viewEvents,
        ILiveUpdateHub events)
    {
        _customers = customers;
        _cases = cases;
        _notifications = notifications;
        _activities = activities;
        _displayIdGenerator = displayIdGenerator;
        _viewEvents = viewEvents;
        _events = events;
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
        Customer c, IReadOnlyList<Notification> accountNotifications,
        IReadOnlyList<CustomerActivity>? accountActivities = null)
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

        // Account-level profile edits (staff + customer self-service). These are
        // the only account events not derivable from the case graph or
        // Notification table, so they come from the CustomerActivities audit table.
        if (accountActivities is not null)
        {
            foreach (var a in accountActivities)
            {
                if (a.AtUtc > (latest ?? DateTime.MinValue))
                {
                    latest = a.AtUtc;
                    desc = a.Label; // "Profile updated"
                    caseId = null;  // account-only — footer shows no deep-link
                }
            }
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
            .AsSplitQuery()
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

        // Batch-load profile-edit audit rows for all loaded customers (avoids
        // per-customer N+1, mirroring the notification batch-load above).
        var custIds = loaded.Select(c => c.Id).ToHashSet();
        var allActs = await _activities.Query()
            .Where(a => custIds.Contains(a.CustomerId))
            .ToListAsync();

        var result = loaded.Select(c =>
        {
            var acctNotes = allNotes.Where(n =>
                n.Recipient == c.Email ||
                (n.CaseId != null && (c.Cases ?? new List<Case>()).Any(cs => cs.Id == n.CaseId.Value)))
                .ToList();
            var acctActs = allActs.Where(a => a.CustomerId == c.Id).ToList();
            return ToDto(c, acctNotes, acctActs);
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
        // Admins reach deleted-detail views (recycle drawer) and must be able
        // to load a soft-deleted row; other roles keep the global filter so
        // binned rows stay hidden.
        var q = _customers.Query();
        if (string.Equals(callerRole, nameof(UserRole.Admin), StringComparison.OrdinalIgnoreCase))
            q = q.IgnoreQueryFilters();
        // A live customer detail must report a case count that matches the list
        // view: only NON-soft-deleted cases. ToDto computes CaseCount from the
        // loaded Cases collection, so we scope every case Include to live cases
        // (IsDeleted == false). We still ignore the *customer* filter so an Admin
        // can open a binned customer's read-only deleted-mode detail — that's the
        // one place a deleted customer is legitimately loaded — without pulling
        // that customer's soft-deleted cases back into the count.
        var c = await q
            .Include(x => x.Account)
            .Include(x => x.Cases.Where(cs => !cs.IsDeleted)).ThenInclude(cs => cs.CallLogs)
            .Include(x => x.Cases.Where(cs => !cs.IsDeleted)).ThenInclude(cs => cs.Comments)
            .Include(x => x.Cases.Where(cs => !cs.IsDeleted)).ThenInclude(cs => cs.Notifications)
            .AsSplitQuery()
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
        var acctActs = await _activities.Query()
            .Where(a => a.CustomerId == id)
            .ToListAsync();

        return ToDto(c, acctNotes, acctActs);
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
            .AsSplitQuery()
            .ToListAsync();

        // Batch-load all relevant notifications once (avoids per-customer N+1).
        var custEmails = loaded.Select(c => c.Email).ToHashSet();
        var caseIds = loaded.SelectMany(c => c.Cases ?? new List<Case>()).Select(cs => cs.Id).ToHashSet();
        var allNotes = await _notifications.Query()
            .Where(n => (n.Recipient != null && custEmails.Contains(n.Recipient))
                     || (n.CaseId != null && caseIds.Contains(n.CaseId.Value)))
            .ToListAsync();

        // Batch-load profile-edit audit rows for all loaded customers (avoids
        // per-customer N+1, mirroring the notification batch-load above).
        var custIds = loaded.Select(c => c.Id).ToHashSet();
        var allActs = await _activities.Query()
            .Where(a => custIds.Contains(a.CustomerId))
            .ToListAsync();

        var result = loaded.Select(c =>
        {
            var acctNotes = allNotes.Where(n =>
                n.Recipient == c.Email ||
                (n.CaseId != null && (c.Cases ?? new List<Case>()).Any(cs => cs.Id == n.CaseId.Value)))
                .ToList();
            var acctActs = allActs.Where(a => a.CustomerId == c.Id).ToList();
            return ToDto(c, acctNotes, acctActs);
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
    public async Task UpdateAsync(UpdateCustomerDto dto, string? callerRole = null, string? callerUserId = null)
    {
        var customer = await _customers.GetByIdAsync(dto.Id)
            ?? throw new KeyNotFoundException($"Customer {dto.Id} not found.");

        // Diff the editable profile fields so the audit row records WHAT changed
        // (not a row for an identical save). Email is normalized the same way on
        // both sides, so a no-op edit writes nothing.
        var changed = new List<string>();
        if (!string.Equals(customer.Name, dto.Name, StringComparison.Ordinal))
            changed.Add("name");
        var newEmail = NormalizeEmail(dto.Email);
        if (!string.Equals(customer.Email, newEmail, StringComparison.Ordinal))
            changed.Add("email");
        var newPhone = NormalizePhone(dto.Phone);
        if (!string.Equals(customer.Phone ?? string.Empty, newPhone ?? string.Empty, StringComparison.Ordinal))
            changed.Add("phone");
        if (!string.Equals(customer.Company ?? string.Empty, dto.Company ?? string.Empty, StringComparison.Ordinal))
            changed.Add("company");
        if (!string.Equals(customer.Address ?? string.Empty, dto.Address ?? string.Empty, StringComparison.Ordinal))
            changed.Add("address");

        customer.Name = dto.Name;
        customer.Email = newEmail;
        customer.Phone = newPhone;
        customer.Company = dto.Company;
        customer.Address = dto.Address;
        // Stamp the profile-edit timestamp once, only when something actually
        // changed. A no-op save (changed.Count == 0) leaves UpdatedAtUtc null,
        // so an unchanged record never appears as "updated since last visit".
        if (changed.Count > 0)
            customer.UpdatedAtUtc = DateTime.UtcNow;
        _customers.Update(customer);
        await _customers.SaveChangesAsync();

        if (changed.Count > 0)
        {
            await _activities.AddAsync(new CustomerActivity
            {
                CustomerId = customer.Id,
                Kind = "account_updated",
                Label = "Profile updated",
                Detail = "Changed: " + string.Join(", ", changed),
                AtUtc = DateTime.UtcNow,
                ActorUserId = callerUserId,
                ActorRole = callerRole,
            });
            await _activities.SaveChangesAsync();

            // ponytail: single in-process broadcast (hub is a singleton fan-out
            // channel). If this ever scales past one instance, swap the hub for a
            // distributed bus and keep this call-site — it's the only one.
            await _events.PublishAsync(new LiveUpdateEvent(
                "customer-update",
                CustomerId: customer.Id,
                ActorUserId: callerUserId,
                ActorRole: callerRole));
        }
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(int id, string? callerRole = null, string? callerUserId = null)
    {
        // Only Admin may delete customers (mirrors CaseService.DeleteAsync).
        if (!string.Equals(callerRole, nameof(UserRole.Admin), StringComparison.OrdinalIgnoreCase))
            throw new ForbiddenException("Only admins can delete customers.");

        // Load the customer with the full graph so we can soft-delete its
        // cases and nullify CaseComment.AuthorCustomerId before the cascade.
        // QueryTracked() (not Query()) so the IsDeleted mutations are tracked
        // and persisted by SaveChangesAsync.
        var customer = await _customers.QueryTracked()
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

        // Soft-delete the customer's cases first (cascade), then the customer
        // itself. No physical removal — EF global soft-delete filters hide
        // these rows from queries while the data remains in the store for
        // audit/recovery. The caller is recorded as the deleter.
        if (customer.Cases is not null)
        {
            foreach (var cs in customer.Cases)
            {
                cs.IsDeleted = true;
                cs.DeletedAtUtc = DateTime.UtcNow;
                cs.DeletedById = callerUserId;
            }
        }

        customer.IsDeleted = true;
        customer.DeletedAtUtc = DateTime.UtcNow;
        customer.DeletedById = callerUserId;

        await _customers.SaveChangesAsync();

        // Audit: record the account deletion in the unified activity log so it
        // shows on the customer's activity panel (mirrors account_updated).
        await _activities.AddAsync(new CustomerActivity
        {
            CustomerId = customer.Id,
            Kind = "account_deleted",
            Label = "Customer deleted",
            Detail = $"Moved to recycle bin{(callerUserId != null ? $" by {callerUserId}" : "")}.",
            AtUtc = DateTime.UtcNow,
            ActorUserId = callerUserId,
            ActorRole = callerRole,
        });
        await _activities.SaveChangesAsync();

        // Push a "customer-deleted" so any open customer grid reflects the
        // recycle-bin move instantly (no manual refresh needed).
        try
        {
            await _events.PublishAsync(new LiveUpdateEvent(
                "customer-deleted", CustomerId: customer.Id, ActorUserId: callerUserId, ActorRole: callerRole));
        }
        catch
        {
            // Swallow — realtime is best-effort.
        }
    }

    /// <inheritdoc/>
    public async Task RestoreAsync(int id, List<int>? caseIdsToRestore = null, string? callerUserId = null)
    {
        // Bypass the global soft-delete filter so we can load a binned
        // customer, then restore it plus (a subset of) its soft-deleted cases.
        // QueryTracked() so the IsDeleted flips are persisted.
        var customer = await _customers.QueryTracked()
            .IgnoreQueryFilters()
            .Include(c => c.Cases)
            .FirstOrDefaultAsync(c => c.Id == id && c.IsDeleted && !c.Purged)
            ?? throw new KeyNotFoundException("Customer is not in the recycle bin (or already purged).");

        // Restore the customer record itself.
        customer.IsDeleted = false;
        customer.DeletedAtUtc = null;
        customer.DeletedById = null;
        customer.RestoredAtUtc = DateTime.UtcNow;
        customer.RestoredById = callerUserId;

        // Restore selected (or all) soft-deleted cases that belong to it.
        // Cases not selected stay in the recycle bin for a later restore.
        // A null selection means "restore all"; an empty array means "restore
        // none" (customer account only). This matches the picker contract, where
        // unchecking every case must NOT bring the cases back.
        var restoreAllCases = caseIdsToRestore is null;
        var restoredCaseCount = 0;
        if (customer.Cases is not null)
        {
            foreach (var cs in customer.Cases)
            {
                if (cs.IsDeleted && !cs.Purged &&
                    (restoreAllCases || caseIdsToRestore!.Contains(cs.Id)))
                {
                    cs.IsDeleted = false;
                    cs.DeletedAtUtc = null;
                    cs.DeletedById = null;
                    restoredCaseCount++;
                }
            }
        }

        // Cases are part of the same customer graph, so a single SaveChanges
        // persists both the customer and the restored cases.
        await _customers.SaveChangesAsync();

        // Audit: record the account restoration in the unified activity log so
        // it shows on the customer's activity panel. Include how many (if any)
        // of its binned cases were brought back with it.
        await _activities.AddAsync(new CustomerActivity
        {
            CustomerId = customer.Id,
            Kind = "account_restored",
            Label = "Customer restored",
            Detail = restoredCaseCount > 0
                ? $"Restored {restoredCaseCount} case{(restoredCaseCount != 1 ? "s" : "")} with the account{(callerUserId != null ? $" by {callerUserId}" : "")}."
                : $"Account restored{(callerUserId != null ? $" by {callerUserId}" : "")}.",
            AtUtc = DateTime.UtcNow,
            ActorUserId = callerUserId,
            ActorRole = null,
        });
        await _activities.SaveChangesAsync();

        // Push a "customer-restored" so any open customer grid (and the recycle
        // bin) reflects the restore instantly (no manual refresh needed).
        try
        {
            await _events.PublishAsync(new LiveUpdateEvent(
                "customer-restored", CustomerId: customer.Id, ActorUserId: callerUserId, ActorRole: null));
        }
        catch
        {
            // Swallow — realtime is best-effort.
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CustomerDto>> GetDeletedAsync()
    {
        // Bypass the global soft-delete filter and return only binned,
        // not-yet-purged customers. Purged rows stay hidden from the bin.
        var binned = await _customers.Query()
            .IgnoreQueryFilters()
            .Include(c => c.Account)
            .Include(c => c.Cases).ThenInclude(cs => cs.Comments)
            .Include(c => c.Cases).ThenInclude(cs => cs.Notifications)
            .AsSplitQuery()
            .Where(c => c.IsDeleted && !c.Purged)
            .OrderByDescending(c => c.DeletedAtUtc)
            .ToListAsync();

        return binned.Select(c =>
        {
            var acctNotes = (IReadOnlyList<Notification>)new List<Notification>();
            var acctActs = (IReadOnlyList<CustomerActivity>?)null;
            var dto = ToDto(c, acctNotes, acctActs);
            // Surface the deleted-state metadata so the UI can render the
            // recycle-bin row without a second call.
            dto.IsDeleted = true;
            dto.DeletedAtUtc = c.DeletedAtUtc;
            dto.DeletedById = c.DeletedById;
            dto.Purged = c.Purged;
            return dto;
        }).ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CaseDto>> GetDeletedCasesAsync(int customerId)
    {
        // Bypass the global soft-delete filter and return only this customer's
        // binned, not-yet-purged cases — the exact set the restore picker offers.
        return await _cases.Query()
            .IgnoreQueryFilters()
            .Include(c => c.Category)
            .Where(c => c.CustomerId == customerId && c.IsDeleted && !c.Purged)
            .OrderByDescending(c => c.DeletedAtUtc)
            .Select(c => CaseService.ToDto(c))
            .ToListAsync();
    }

    /// <inheritdoc/>
    public async Task PurgeAsync(int id, string? callerRole = null)
    {
        // Only Admin may purge (irreversible PII erasure).
        if (!string.Equals(callerRole, nameof(UserRole.Admin), StringComparison.OrdinalIgnoreCase))
            throw new ForbiddenException("Only admins can purge customers.");

        // Load the binned customer with its full graph so we can anonymize the
        // profile, disable the account credentials, nullify comment authorship,
        // and reach the linked cases. IgnoreQueryFilters bypasses the soft-delete
        // filter so a binned (IsDeleted) customer is still loadable. QueryTracked()
        // so the anonymization is persisted.
        var target = await _customers.QueryTracked()
            .IgnoreQueryFilters()
            .Include(c => c.Cases).ThenInclude(cs => cs.Comments)
            .Include(c => c.Account)
            .FirstOrDefaultAsync(c => c.Id == id && c.IsDeleted && !c.Purged)
            ?? throw new KeyNotFoundException("Customer is not in the recycle bin (or already purged).");

        // Capture the email FIRST — it's the key used to find linked notifications,
        // and we're about to null it on the profile.
        var email = target.Email;

        // Anonymize the profile PII. CustomerDisplayId is intentionally kept for
        // traceability/audit; it is not PII. Email is a non-nullable string
        // (initialized to string.Empty), so we blank it rather than null it.
        target.Name = "Deleted User";
        target.Email = string.Empty;
        target.Phone = null;
        target.Company = null;
        target.Address = null;

        // Disable the login entirely: a purged account must never authenticate
        // again. The row stays (FK integrity + audit), but the credentials are gone.
        if (target.Account is not null)
        {
            target.Account.PasswordHash = null;
            target.Account.InviteToken = null;
            target.Account.IsActive = false;
            target.Account.ActivatedAtUtc = null;
        }

        // Nullify customer-authored comment links (text is preserved). DeleteAsync
        // usually already did this during the cascade, but this is defensive and
        // idempotent in case a comment slipped through.
        if (target.Cases is not null)
        {
            foreach (var cs in target.Cases)
            {
                if (cs.Comments is null) continue;
                foreach (var comment in cs.Comments)
                    if (comment.AuthorCustomerId == id)
                        comment.AuthorCustomerId = null;
            }
        }

        // Scrub notification PII: any notification addressed to the customer's
        // email, or tied to one of the customer's cases.
        if (!string.IsNullOrEmpty(email))
        {
            // EF can't translate an in-memory collection predicate inside the
            // query, so materialize the customer's case ids locally first.
            var customerCaseIds = (target.Cases ?? new List<Case>()).Select(cs => cs.Id).ToList();
            var notes = await _notifications.Query().IgnoreQueryFilters()
                .Where(n => n.Recipient == email ||
                            (n.CaseId != null && customerCaseIds.Contains(n.CaseId.Value)))
                .ToListAsync();
            foreach (var n in notes)
                n.Recipient = null;
            if (notes.Count > 0)
                await _notifications.SaveChangesAsync();
        }

        // Cascade the purge to this customer's own binned (soft-deleted but not
        // yet purged) cases. A purged customer is unrecoverable, so any of its
        // deleted cases must also be purged + scrubbed — otherwise they'd linger
        // in the case recycle-bin forever, un-restorable, with a misleading
        // "restore the customer first" dead-end. Mirrors PurgeCaseAsync's scrub.
        if (target.Cases is not null)
        {
            foreach (var cs in target.Cases.Where(cs => cs.IsDeleted && !cs.Purged))
            {
                cs.Subject = "[deleted]";
                cs.Description = "[deleted]";
                cs.Purged = true;
                cs.PurgedAtUtc = DateTime.UtcNow;
            }
        }

        // Mark purged (excludes this row from future recycle-bin queries).
        target.Purged = true;
        target.PurgedAtUtc = DateTime.UtcNow;

        // Customer + account + cases + comments are one graph; a single
        // SaveChanges persists the whole anonymized row.
        await _customers.SaveChangesAsync();
    }

    /// <summary>
    /// Returns every email sent to this customer: account-level invites/resets/
    /// manual emails (Recipient == customer.Email, CaseId null) plus any case
    /// emails (CaseId belongs to this customer). Newest first.
    /// </summary>
    public async Task<IReadOnlyList<NotificationDto>> GetCustomerEmailsAsync(int customerId, string? callerRole = null, string? callerUserId = null)
    {
        // Mirror GetCustomerActivityAsync: resolve a soft-deleted customer too
        // (the controller's GetById check already passed via IgnoreQueryFilters),
        // so an Admin viewing a deleted customer's recycle-bin emails works.
        var c = await _customers.Query()
            .IgnoreQueryFilters()
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
            // Mirror GetByIdAsync: an Admin viewing a soft-deleted customer's
            // recycle-bin activity must still resolve it (the controller's
            // GetById check already passed via IgnoreQueryFilters). Without
            // this, a deleted customer's detail page throws "Customer N not
            // found." and the middleware logs it as an Unhandled exception.
            .AsSplitQuery()
            .IgnoreQueryFilters()
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

        // Account-level profile edits (staff + customer self-service). These are
        // the only account events NOT derivable from the case graph or
        // Notification table, so they live in the CustomerActivities audit table.
        var profileEdits = await _activities.Query()
            .Where(a => a.CustomerId == customerId)
            .ToListAsync();
        foreach (var a in profileEdits)
        {
            items.Add(new CustomerActivityItemDto
            {
                // Negative id keeps these distinct from case-item ids and from
                // the activation sentinel (-1) used below.
                Id = -1000 - a.Id,
                Kind = a.Kind,
                Label = a.Label,
                Detail = a.Detail,
                AtUtc = a.AtUtc,
                CaseId = a.CaseId,
                Who = a.ActorRole,
            });
        }

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

        // Viewed/opened audit rows: account views + views of this customer's
        // cases. A read is a real activity-panel entry (shows who opened the
        // record and when) but is deliberately NOT folded into ComputeLastActivity
        // — that footer drives customer-list sort order and a read shouldn't make
        // a customer jump to the top just because someone opened it.
        var customerCaseIds = (c.Cases ?? new List<Case>()).Select(cs => cs.Id).ToList();
        var viewEvents = await _viewEvents.GetForCustomerAsync(customerId, customerCaseIds);
        foreach (var v in viewEvents)
        {
            items.Add(new CustomerActivityItemDto
            {
                Id = -2000 - v.Id, // negative, distinct from case items (-) and profile edits (-1000-)
                Kind = "viewed",
                Label = "Viewed",
                Detail = $"Viewed by {v.ViewerName}",
                AtUtc = v.AtUtc,
                CaseId = v.TargetType == "Case" ? v.TargetId : null,
                Who = v.ViewerRole,
            });
        }

        return items
            .OrderByDescending(i => i.AtUtc)
            .ToList();
    }

    private static CustomerDto ToDto(Customer c, IReadOnlyList<Notification> accountNotifications,
        IReadOnlyList<CustomerActivity>? accountActivities = null)
    {
        var (lastActivityAt, lastActivityDesc, lastActivityCaseId) = ComputeLastActivity(c, accountNotifications, accountActivities);
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
            UpdatedAtUtc = c.UpdatedAtUtc,
            LastActivityAtUtc = lastActivityAt,
            LastActivityDescription = lastActivityDesc,
            LastActivityCaseId = lastActivityCaseId,
            HasAccount = c.Account != null,
            AccountActive = c.Account != null && c.Account.IsActive,
            // Soft-delete / recycle metadata so the UI can render deleted-mode
            // detail without a second call.
            IsDeleted = c.IsDeleted,
            DeletedAtUtc = c.DeletedAtUtc,
            DeletedById = c.DeletedById,
            Purged = c.Purged,
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
