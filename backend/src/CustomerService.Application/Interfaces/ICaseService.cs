using CustomerService.Application.Dtos;
using CustomerService.Domain.Entities;

namespace CustomerService.Application.Interfaces;

/// <summary>Application service contract for case operations.</summary>
public interface ICaseService
{
    /// <summary>Returns cases, optionally filtered.</summary>
    /// <param name="status">Optional status filter.</param>
    /// <param name="priority">Optional priority filter.</param>
    /// <param name="categoryId">Optional category filter.</param>
    /// <param name="from">Optional created-from date (UTC).</param>
    /// <param name="to">Optional created-to date (UTC).</param>
    /// <param name="overdue">When true, only open cases with a past follow-up deadline and no follow-up since.</param>
    /// <param name="assignedToUserId">When set, only cases assigned to this user id (resolved from the JWT by the controller).</param>
    /// <param name="callerRole">Role of the calling user (Admin sees everything; Agent is server-side scoped to their own + unassigned cases).</param>
    /// <param name="callerUserId">Id of the calling user (used to scope an Agent's view).</param>
    /// <returns>Matching cases.</returns>
    Task<IReadOnlyList<CaseDto>> GetAllAsync(CaseStatus? status, Priority? priority, int? categoryId, DateTime? from, DateTime? to, bool overdue = false, string? assignedToUserId = null, string? callerRole = null, string? callerUserId = null);

    /// <summary>Returns a single case by id.</summary>
    /// <param name="id">Case id.</param>
    /// <param name="callerRole">Role of the calling user (Admin sees everything; Agent is blocked from cases assigned to another agent).</param>
    /// <param name="callerUserId">Id of the calling user (used to scope an Agent's view).</param>
    /// <returns>The <see cref="CaseDto"/> or null.</returns>
    Task<CaseDto?> GetByIdAsync(int id, string? callerRole = null, string? callerUserId = null);

    /// <summary>Creates a case, auto-suggesting priority via ML when not supplied.</summary>
    /// <param name="dto">Create payload.</param>
    /// <returns>The created <see cref="CaseDto"/>.</returns>
    Task<CaseDto> CreateAsync(CreateCaseDto dto);

    /// <summary>Updates a case (priority override allowed).</summary>
    /// <param name="id">Case id.</param>
    /// <param name="dto">Update payload.</param>
    /// <param name="callerRole">Role of the calling user (Admin may reassign; Agent may only edit their own assigned case and may not change assignment).</param>
    /// <param name="callerUserId">Id of the calling user (used to scope an Agent's writes).</param>
    Task UpdateAsync(int id, UpdateCaseDto dto, string? callerRole = null, string? callerUserId = null);

    /// <summary>Deletes a case.</summary>
    /// <param name="id">Case id.</param>
    Task DeleteAsync(int id);

    /// <summary>
    /// Returns the agent's "Messages" conversations: cases assigned to the
    /// given agent that have at least one comment, each summarised with the
    /// latest comment (snippet, timestamp, author) and an <c>unread</c> flag.
    /// The unread flag is derived by comparing the latest comment timestamp to
    /// the agent's per-case "last viewed" marker (<see cref="ConversationReadState"/>);
    /// if the agent has never viewed the conversation, it is unread.
    /// </summary>
    /// <param name="agentUserId">The calling agent's User.Id (from the JWT).</param>
    /// <returns>Conversation summaries, most-recent activity first.</returns>
    Task<IReadOnlyList<ConversationSummaryDto>> GetMyConversationsAsync(string agentUserId);

    /// <summary>
    /// Marks a conversation as read for the given agent. Upserts a
    /// <see cref="ConversationReadState"/> record with the current UTC time
    /// so that subsequent calls to <see cref="GetMyConversationsAsync"/> will
    /// no longer flag this case as unread.
    /// </summary>
    /// <param name="caseId">The case whose conversation to mark as read.</param>
    /// <param name="agentUserId">The agent's User.Id (from the JWT).</param>
    Task MarkConversationReadAsync(int caseId, string agentUserId);

    /// <summary>
    /// Returns all cases that have at least one comment, regardless of
    /// assignment. Each entry includes the assigned agent name (or null for
    /// unassigned cases). Intended for the Admin "Conversations" view.
    /// </summary>
    /// <returns>Conversation summaries, most-recent activity first.</returns>
    Task<IReadOnlyList<ConversationSummaryDto>> GetAllConversationsAsync();
}
