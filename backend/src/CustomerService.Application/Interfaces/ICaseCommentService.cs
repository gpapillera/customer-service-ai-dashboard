using CustomerService.Application.Dtos;
using CustomerService.Domain.Entities;

namespace CustomerService.Application.Interfaces;

/// <summary>
/// Application service contract for the shared case-comment thread. The same
/// underlying <see cref="CaseComment"/> data is reached by customers (scoped to
/// their own cases) and by staff (any case) through two different controllers;
/// this service holds the shared read/post logic.
/// </summary>
public interface ICaseCommentService
{
    /// <summary>Returns the comment thread for a case, ordered oldest-first.</summary>
    /// <param name="caseId">Case id.</param>
    /// <returns>The comments, or null if the case does not exist.</returns>
    Task<IReadOnlyList<CaseCommentDto>?> GetCommentsAsync(int caseId);

    /// <summary>
    /// Posts a comment authored by a STAFF user. Enforces that exactly one
    /// author is set (staff here). Throws if the case does not exist or the
    /// body is empty/whitespace.
    /// </summary>
    /// <param name="caseId">Case id.</param>
    /// <param name="authorUserId">Staff user id (from the JWT).</param>
    /// <param name="body">Comment body.</param>
    Task<CaseCommentDto> AddStaffCommentAsync(int caseId, string authorUserId, string body);

    /// <summary>
    /// Posts a comment authored by a CUSTOMER. Enforces that exactly one author
    /// is set (customer here). Throws if the case does not exist or the body is
    /// empty/whitespace.
    /// </summary>
    /// <param name="caseId">Case id.</param>
    /// <param name="authorCustomerId">Customer id (from the JWT).</param>
    /// <param name="body">Comment body.</param>
    Task<CaseCommentDto> AddCustomerCommentAsync(int caseId, int authorCustomerId, string body);
}
