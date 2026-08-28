namespace CustomerService.Application.Dtos;

/// <summary>
/// One real-time event covering EVERY staff (and customer self-service) mutation,
/// broadcast over SSE so every other logged-in staff view refreshes instantly
/// instead of waiting for a manual refresh. Replaces the previous case-assignment
/// / customer-update split. Targets are optional: an assignment carries
/// <see cref="CaseId"/> + <see cref="AssignedToUserId"/>; a customer edit carries
/// <see cref="CustomerId"/>; a null target means "a global change happened" — e.g.
/// a delete/restore/status/priority/comment/email — and subscribers that don't care
/// about that scope simply re-fetch the list they already show.
/// </summary>
/// <param name="Kind">
///   Discriminator: <c>case-assignment</c> | <c>case-update</c> |
///   <c>customer-update</c> | <c>customer-deleted</c> | <c>customer-restored</c>.
/// </param>
/// <param name="CaseId">Id of the affected case, when the event is case-scoped.</param>
/// <param name="CustomerId">Id of the affected customer, when customer-scoped.</param>
/// <param name="ActorUserId">Staff/customer id who performed the mutation.</param>
/// <param name="ActorRole">Role of the actor (<c>Admin</c>/<c>Agent</c>/<c>Customer</c>).</param>
/// <param name="AssignedToUserId">
///   For <c>case-assignment</c>: the user the case is now assigned to (null = unassigned).
/// </param>
public sealed record LiveUpdateEvent(
    string Kind,
    int? CaseId = null,
    int? CustomerId = null,
    string? ActorUserId = null,
    string? ActorRole = null,
    string? AssignedToUserId = null);
