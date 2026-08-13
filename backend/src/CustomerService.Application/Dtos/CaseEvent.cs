namespace CustomerService.Application.Dtos;

/// <summary>
/// Real-time notification that a case's assignment (or unassignment) changed.
/// Broadcast over SSE so the agent/admin UI can reflect it instantly instead of
/// polling. <see cref="AssignedToUserId"/> is null when the case was moved to
/// Unassigned (which, per the Agent scoping rule in <c>CaseService</c>, becomes
/// visible to BOTH agents).
/// </summary>
public sealed record CaseEvent(int CaseId, string? AssignedToUserId, string Type);
