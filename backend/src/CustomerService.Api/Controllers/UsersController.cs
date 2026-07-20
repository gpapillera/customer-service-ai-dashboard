using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CustomerService.Api.Controllers;

/// <summary>
/// Read-only endpoints for application users (agents/admins). Used by the
/// case form to populate the "Assignee" dropdown and by the admin Agents
/// list. See docs/DIY.md §6.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Agent")]
public class UsersController : ControllerBase
{
    private readonly IRepository<User> _users;
    private readonly IRepository<Case> _cases;

    /// <summary>Initializes a new <see cref="UsersController"/>.</summary>
    /// <param name="users">User repository.</param>
    /// <param name="cases">Case repository (used for open-case aggregates).</param>
    public UsersController(IRepository<User> users, IRepository<Case> cases)
    {
        _users = users;
        _cases = cases;
    }

    /// <summary>Lists all users (agents + admins) for assignment pickers.</summary>
    /// <returns>The users as lightweight <see cref="AgentSummary"/> records.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IReadOnlyList<AgentSummary> GetAll()
        => _users.Query()
            .OrderBy(u => u.FullName)
            .Select(u => new AgentSummary(u.Id, u.FullName, u.Role.ToString(), 0))
            .ToList();

    /// <summary>
    /// Lists every <see cref="UserRole.Agent"/> with the count of cases
    /// currently assigned to them that are still open (not Resolved/Closed).
    /// The count is computed as a real aggregate query in the database, not by
    /// fetching every case to the client. Agents may read this too (useful
    /// context), but only admins get the management-flavored UI.
    /// </summary>
    /// <returns>One <see cref="AgentSummary"/> per agent, with <see cref="AgentSummary.OpenCaseCount"/>.</returns>
    [HttpGet("agents-summary")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<AgentSummary>> GetAgentsSummary()
    {
        var openStatuses = new[] { (int)CaseStatus.New, (int)CaseStatus.InProgress, (int)CaseStatus.Escalated };
        var agents = await _users.Query()
            .Where(u => u.Role == UserRole.Agent)
            .OrderBy(u => u.FullName)
            .Select(u => new { u.Id, u.FullName, u.Role })
            .ToListAsync();

        // Real aggregate: count open cases per agent directly in the database.
        var counts = await _cases.Query()
            .Where(c => c.AssignedToUserId != null && openStatuses.Contains((int)c.Status))
            .GroupBy(c => c.AssignedToUserId)
            .Select(g => new { AgentId = g.Key, Count = g.Count() })
            .ToListAsync();

        var countById = counts.Where(x => x.AgentId != null)
                               .ToDictionary(x => x.AgentId!, x => x.Count);

        return agents
            .Select(a => new AgentSummary(
                a.Id,
                a.FullName,
                a.Role.ToString(),
                countById.TryGetValue(a.Id, out var n) ? n : 0))
            .ToList();
    }
}

/// <summary>Lightweight user summary for assignment dropdowns and the agents list.</summary>
/// <param name="Id">User primary key (GUID string).</param>
/// <param name="FullName">Display name.</param>
/// <param name="Role">Role name (Admin or Agent).</param>
/// <param name="OpenCaseCount">Number of currently-open cases assigned to this user (agents only).</param>
public record AgentSummary(string Id, string FullName, string Role, int OpenCaseCount);
