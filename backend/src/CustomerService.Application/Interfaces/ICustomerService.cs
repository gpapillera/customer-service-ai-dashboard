using CustomerService.Application.Dtos;

namespace CustomerService.Application.Interfaces;

/// <summary>Application service contract for customer operations.</summary>
public interface ICustomerService
{
    /// <summary>Returns all customers (with case counts).</summary>
    /// <param name="callerRole">Role of the calling user (Admin sees all; Agent is scoped to customers who share at least one case with them).</param>
    /// <param name="callerUserId">Id of the calling user (used to scope an Agent's view).</param>
    /// <returns>List of <see cref="CustomerDto"/>.</returns>
    Task<IReadOnlyList<CustomerDto>> GetAllAsync(string? callerRole = null, string? callerUserId = null,
        bool? hasAccount = null, string? sortBy = null, string? sortDirection = null);

    /// <summary>Returns a single customer by id.</summary>
    /// <param name="id">Customer id.</param>
    /// <param name="callerRole">Role of the calling user (Admin sees all; Agent is blocked from customers they don't share a case with).</param>
    /// <param name="callerUserId">Id of the calling user (used to scope an Agent's view).</param>
    /// <returns>The <see cref="CustomerDto"/> or null.</returns>
    Task<CustomerDto?> GetByIdAsync(int id, string? callerRole = null, string? callerUserId = null);

    /// <summary>Searches customers by name/email/phone substring.</summary>
    /// <param name="term">Search term (case-insensitive).</param>
    /// <param name="callerRole">Role of the calling user (Admin sees all; Agent is scoped to customers who share at least one case with them).</param>
    /// <param name="callerUserId">Id of the calling user (used to scope an Agent's view).</param>
    /// <returns>Matching customers.</returns>
    Task<IReadOnlyList<CustomerDto>> SearchAsync(string? term, string? callerRole = null, string? callerUserId = null,
        bool? hasAccount = null, string? sortBy = null, string? sortDirection = null);

    /// <summary>Returns a customer's case history, scoped to the caller (an Agent only sees cases assigned to them).</summary>
    /// <param name="customerId">Customer id.</param>
    /// <param name="callerRole">Role of the calling user.</param>
    /// <param name="callerUserId">Id of the calling user (used to scope an Agent's view).</param>
    /// <returns>The customer's cases visible to the caller.</returns>
    Task<IReadOnlyList<CaseDto>> GetCustomerCaseHistoryAsync(int customerId, string? callerRole = null, string? callerUserId = null);

    /// <summary>Returns every email sent to a customer (account invites/resets/manual + case emails), newest first.</summary>
    /// <param name="customerId">Customer id.</param>
    /// <param name="callerRole">Role of the calling user (Agent is scoped to customers they share a case with).</param>
    /// <param name="callerUserId">Id of the calling user (used to scope an Agent's view).</param>
    /// <returns>The customer's emails visible to the caller.</returns>
    Task<IReadOnlyList<NotificationDto>> GetCustomerEmailsAsync(int customerId, string? callerRole = null, string? callerUserId = null);

    /// <summary>Returns the merged case + account activity timeline for a customer, newest first (includes account events even with no cases).</summary>
    /// <param name="customerId">Customer id.</param>
    /// <param name="callerRole">Role of the calling user (Agent is scoped to customers they share a case with).</param>
    /// <param name="callerUserId">Id of the calling user (used to scope an Agent's view).</param>
    /// <returns>The customer's activity timeline visible to the caller.</returns>
    Task<IReadOnlyList<CustomerActivityItemDto>> GetCustomerActivityAsync(int customerId, string? callerRole = null, string? callerUserId = null);

    /// <summary>Creates a customer.</summary>
    /// <param name="dto">Create payload.</param>
    /// <returns>The created <see cref="CustomerDto"/>.</returns>
    Task<CustomerDto> CreateAsync(CreateCustomerDto dto);

    /// <summary>Updates a customer.</summary>
    /// <param name="dto">Update payload (must include id).</param>
    /// <param name="callerRole">Role of the calling user (recorded on the activity audit row).</param>
    /// <param name="callerUserId">Id of the calling user (recorded on the activity audit row).</param>
    Task UpdateAsync(UpdateCustomerDto dto, string? callerRole = null, string? callerUserId = null);

    /// <summary>Soft-deletes a customer (Admin only) and cascades the soft-delete to its cases.</summary>
    /// <param name="id">Customer id.</param>
    /// <param name="callerRole">Role of the calling user — only Admin is allowed.</param>
    /// <param name="callerUserId">Id of the calling user (recorded as the deleter on the soft-delete audit fields).</param>
    Task DeleteAsync(int id, string? callerRole = null, string? callerUserId = null);

    /// <summary>
    /// Restores a soft-deleted customer from the recycle bin, optionally
    /// restoring a selected subset of its soft-deleted cases. Cases not
    /// selected remain soft-deleted. Bypasses the global soft-delete filter
    /// so the binned customer can be loaded.
    /// </summary>
    /// <param name="id">Customer id (must be soft-deleted and not purged).</param>
    /// <param name="caseIdsToRestore">Case ids to restore. <c>null</c> restores all soft-deleted cases.</param>
    /// <param name="callerUserId">Id of the calling user (recorded as the restorer).</param>
    Task RestoreAsync(int id, List<int>? caseIdsToRestore = null, string? callerUserId = null);

    /// <summary>
    /// Returns the soft-deleted customers in the recycle bin (Admin only).
    /// Purged rows are excluded — they have been anonymized and are not
    /// restorable. Bypasses the global soft-delete filter.
    /// </summary>
    /// <returns>Binned customers that are <c>IsDeleted &amp;&amp; !Purged</c>.</returns>
    Task<IReadOnlyList<CustomerDto>> GetDeletedAsync();

    /// <summary>
    /// Returns the soft-deleted (non-purged) cases belonging to a specific
    /// customer — used by the account-restore case-picker dialog so an admin
    /// can choose which binned cases to bring back. Admin only.
    /// </summary>
    /// <param name="customerId">Customer id.</param>
    /// <returns>Binned cases for that customer (<c>IsDeleted &amp;&amp; !Purged</c>), newest first.</returns>
    Task<IReadOnlyList<CaseDto>> GetDeletedCasesAsync(int customerId);

    /// <summary>
    /// Permanently erases a soft-deleted customer's PII (GDPR-style erasure).
    /// The row is kept for referential integrity and audit, but the profile
    /// fields are anonymized, the account credentials are disabled (so the
    /// login can never be reused), and any linked notification recipient
    /// addresses are scrubbed. Irreversible. Admin only.
    /// </summary>
    /// <param name="id">Customer id (must be soft-deleted and not already purged).</param>
    /// <param name="callerRole">Role of the calling user — only Admin may purge.</param>
    Task PurgeAsync(int id, string? callerRole = null);
}
