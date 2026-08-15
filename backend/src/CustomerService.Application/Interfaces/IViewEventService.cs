using CustomerService.Application.Dtos;

namespace CustomerService.Application.Interfaces;

/// <summary>
/// Records and reads "viewed/opened" audit events for Case and Customer detail
/// pages. A view is a pure read event (distinct from the edit audit in
/// <see cref="ICustomerActivityService"/>): it is coalesced per viewer by a
/// cooldown so page refreshes/back-navigations don't flood the timeline.
/// </summary>
public interface IViewEventService
{
    /// <summary>
    /// Records that <paramref name="viewerName"/> viewed the target, unless the
    /// same viewer already has a <c>ViewEvent</c> for that target within the
    /// cooldown window (default 10 minutes). Returns the created row, or null if
    /// the call was coalesced (an existing recent view covers it).
    /// </summary>
    /// <param name="targetType">"Case" or "Customer".</param>
    /// <param name="targetId">Id of the viewed Case or Customer.</param>
    /// <param name="viewerUserId">Viewer's user id (JWT sub) for staff; null for customer self-view.</param>
    /// <param name="viewerName">Human-readable viewer name shown on the timeline.</param>
    /// <param name="viewerRole">Viewer role ("Admin"/"Agent"/"Customer"); null if unknown.</param>
    /// <param name="now">Override for the event timestamp (defaults to UtcNow). Used by tests to pin the cooldown window.</param>
    Task<ViewEventDto?> RecordViewAsync(string targetType, int targetId, string? viewerUserId, string viewerName, string? viewerRole, DateTime? now = null);

    /// <summary>All view events for a single target, newest first.</summary>
    Task<IReadOnlyList<ViewEventDto>> GetForTargetAsync(string targetType, int targetId);

    /// <summary>
    /// View events for a customer: account-level views plus views of any of the
    /// customer's cases. Newest first, so the customer activity panel can show
    /// "opened a case" entries alongside "opened the account".
    /// </summary>
    Task<IReadOnlyList<ViewEventDto>> GetForCustomerAsync(int customerId, IReadOnlyList<int> caseIds);
}
