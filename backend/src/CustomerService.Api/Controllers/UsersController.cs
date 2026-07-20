using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerService.Api.Controllers;

/// <summary>
/// Read-only endpoints for application users (agents/admins). Used by the
/// case form to populate the "Assignee" dropdown. See docs/DIY.md §6.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IRepository<User> _users;

    /// <summary>Initializes a new <see cref="UsersController"/>.</summary>
    /// <param name="users">User repository.</param>
    public UsersController(IRepository<User> users) => _users = users;

    /// <summary>Lists all users (agents + admins) for assignment pickers.</summary>
    /// <returns>The users as lightweight <see cref="AgentSummary"/> records.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IReadOnlyList<AgentSummary> GetAll()
        => _users.Query()
            .OrderBy(u => u.FullName)
            .Select(u => new AgentSummary(u.Id, u.FullName, u.Role.ToString()))
            .ToList();
}

/// <summary>Lightweight user summary for assignment dropdowns.</summary>
/// <param name="Id">User primary key (GUID string).</param>
/// <param name="FullName">Display name.</param>
/// <param name="Role">Role name (Admin or Agent).</param>
public record AgentSummary(string Id, string FullName, string Role);
