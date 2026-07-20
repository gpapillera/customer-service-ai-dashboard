using System.Security.Claims;
using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerService.Api.Controllers;

/// <summary>
/// Dashboard summary endpoint (KPIs + trends + category breakdown).
/// The same endpoint is used by both roles; an Agent sees only the cases
/// assigned to them (scoped by their JWT user id), while an Admin sees the
/// company-wide totals. See docs/DIY.md §8 for the dashboard walkthrough.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Agent")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _service;

    /// <summary>Initializes a new <see cref="DashboardController"/>.</summary>
    /// <param name="service">Dashboard service.</param>
    public DashboardController(IDashboardService service) => _service = service;

    /// <summary>
    /// Returns the full dashboard payload. For an Agent caller, every number and
    /// chart is scoped to cases assigned to that agent (id taken from the JWT,
    /// never a query param). An Admin caller gets the company-wide view.
    /// </summary>
    /// <returns>A <see cref="DashboardDto"/>.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<DashboardDto> Get()
    {
        // Agents are scoped to their own assigned cases; Admins see everything.
        var agentId = User.IsInRole("Agent")
            ? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            : null;
        return await _service.GetDashboardAsync(agentId);
    }
}
