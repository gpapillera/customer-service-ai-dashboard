using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using CustomerService.Domain;
using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CustomerService.Api.Controllers;

/// <summary>
/// Endpoints for application users (agents/admins). Includes read-only
/// list/summary endpoints, the signed-in user's own profile management,
/// password-reset request (Phase 10), and admin agent management (Phase 11).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Agent")]
public class UsersController : ControllerBase
{
    private readonly IRepository<User> _users;
    private readonly IRepository<Case> _cases;
    private readonly IAuthService _auth;
    private readonly IDashboardService _dashboardService;

    /// <summary>Initializes a new <see cref="UsersController"/>.</summary>
    public UsersController(IRepository<User> users, IRepository<Case> cases, IAuthService auth, IDashboardService dashboardService)
    {
        _users = users;
        _cases = cases;
        _auth = auth;
        _dashboardService = dashboardService;
    }

    // ── Staff profile endpoints (Phase 10) ──

    /// <summary>Returns the signed-in staff member's own profile.</summary>
    [HttpGet("me")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<StaffProfileDto>> GetMyProfile()
    {
        var userId = GetUserId();
        return Ok(await _auth.GetProfileAsync(userId));
    }

    /// <summary>Updates the signed-in staff member's own name (email read-only).</summary>
    [HttpPut("me")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateMyProfile([FromBody] UpdateStaffProfileDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        await _auth.UpdateProfileAsync(GetUserId(), dto);
        return NoContent();
    }

    /// <summary>Requests a password-reset email for the signed-in staff member.</summary>
    [HttpPost("me/request-password-reset")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RequestPasswordReset()
    {
        await _auth.RequestPasswordResetAsync(GetUserId());
        return NoContent();
    }

    private string GetUserId()
        => User.FindFirst(ClaimTypes.NameIdentifier)?.Value
           ?? throw new UnauthorizedAccessException();

    // ── Admin agent-management endpoints (Phase 11) ──

    /// <summary>Admin edits an agent's name and email. No role or password changes.</summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAgent(string id, [FromBody] UpdateAgentDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var user = await _users.Query().FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();
        user.FullName = dto.FullName;
        user.Email = dto.Email;
        _users.Update(user);
        await _users.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Returns the full KPI set for any agent (admin only).\summary>
    [HttpGet("{id}/kpis")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DashboardDto>> GetAgentKpis(string id)
    {
        var user = await _users.Query().FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();
        // Reuse the same dashboard service — agentId scopes the "My *" fields.
        var dto = await _dashboardService.GetDashboardAsync(id);
        return Ok(dto);
    }

    // ── Existing list/summary endpoints ──

    /// <summary>Lists all users (agents + admins) for assignment pickers.</summary>
    /// <returns>The users as lightweight <see cref="AgentSummary"/> records.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IReadOnlyList<AgentSummary> GetAll()
        => _users.Query()
            .OrderBy(u => u.FullName)
            .Select(u => new AgentSummary(u.Id, u.FullName, u.Email, u.Role.ToString(), 0, u.AgentDisplayId, u.ProfilePictureUrl))
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
            .Select(u => new { u.Id, u.FullName, u.Email, u.Role, u.AgentDisplayId, u.ProfilePictureUrl })
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
                a.Email,
                a.Role.ToString(),
                countById.TryGetValue(a.Id, out var n) ? n : 0,
                a.AgentDisplayId,
                a.ProfilePictureUrl))
            .ToList();
    }

    /// <summary>
    /// Returns a per-agent workload summary for the Admin dashboard Agent
    /// Workload section. Includes open, high-priority, resolved, and overdue
    /// follow-up counts for every agent — computed in a single round-trip.
    /// Admin-only.
    /// </summary>
    [HttpGet("agent-workload")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<AgentWorkloadDto>> GetAgentWorkload()
    {
        var agents = await _users.Query()
            .Where(u => u.Role == UserRole.Agent)
            .OrderBy(u => u.FullName)
            .Select(u => new { u.Id, u.FullName })
            .ToListAsync();

        var openStatuses = new[] { (int)CaseStatus.New, (int)CaseStatus.InProgress, (int)CaseStatus.Escalated };
        var allCases = _cases.Query().Where(c => c.AssignedToUserId != null);

        // Open case count per agent
        var openCounts = await allCases
            .Where(c => openStatuses.Contains((int)c.Status))
            .GroupBy(c => c.AssignedToUserId)
            .Select(g => new { AgentId = g.Key!, Count = g.Count() })
            .ToListAsync();

        // High-priority count per agent
        var highCounts = await allCases
            .Where(c => c.Priority == Domain.Entities.Priority.High)
            .GroupBy(c => c.AssignedToUserId)
            .Select(g => new { AgentId = g.Key!, Count = g.Count() })
            .ToListAsync();

        // Resolved count per agent
        var resolvedCounts = await allCases
            .Where(c => c.Status == CaseStatus.Resolved)
            .GroupBy(c => c.AssignedToUserId)
            .Select(g => new { AgentId = g.Key!, Count = g.Count() })
            .ToListAsync();

        // Overdue follow-up counts — load all open cases with includes, then
        // evaluate OverduePolicy.NeedsFollowUp in memory (same approach as
        // GetOverdueFollowUpsAsync in DashboardRepository).
        var now = DateTime.UtcNow;
        var openCases = await _cases.Query()
            .Include(c => c.CallLogs)
            .Where(c => openStatuses.Contains((int)c.Status) && c.AssignedToUserId != null)
            .ToListAsync();

        var overdueByAgent = openCases
            .Where(c => OverduePolicy.NeedsFollowUp(c, now))
            .GroupBy(c => c.AssignedToUserId)
            .ToDictionary(g => g.Key!, g => g.Count());

        var openById = openCounts.ToDictionary(x => x.AgentId, x => x.Count);
        var highById = highCounts.ToDictionary(x => x.AgentId, x => x.Count);
        var resolvedById = resolvedCounts.ToDictionary(x => x.AgentId, x => x.Count);

        return agents
            .Select(a => new AgentWorkloadDto
            {
                AgentId = a.Id,
                FullName = a.FullName,
                OpenCaseCount = openById.TryGetValue(a.Id, out var oc) ? oc : 0,
                HighPriorityCount = highById.TryGetValue(a.Id, out var hc) ? hc : 0,
                ResolvedCount = resolvedById.TryGetValue(a.Id, out var rc) ? rc : 0,
                OverdueCount = overdueByAgent.TryGetValue(a.Id, out var od) ? od : 0,
            })
            .ToList();
    }
}

/// <summary>Lightweight user summary for assignment dropdowns and the agents list.</summary>
/// <param name="Id">User primary key (GUID string).</param>
/// <param name="FullName">Display name.</param>
/// <param name="Email">Login email.</param>
/// <param name="Role">Role name (Admin or Agent).</param>
/// <param name="OpenCaseCount">Number of currently-open cases assigned to this user (agents only).</param>
public record AgentSummary(string Id, string FullName, string Email, string Role, int OpenCaseCount, string? AgentDisplayId = null, string? ProfilePictureUrl = null);
