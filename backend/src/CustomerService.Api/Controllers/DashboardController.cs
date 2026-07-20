using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerService.Api.Controllers;

/// <summary>
/// Dashboard summary endpoint (KPIs + trends + category breakdown).
/// See docs/DIY.md §8 for the dashboard + Chart.js walkthrough.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _service;

    /// <summary>Initializes a new <see cref="DashboardController"/>.</summary>
    /// <param name="service">Dashboard service.</param>
    public DashboardController(IDashboardService service) => _service = service;

    /// <summary>Returns the full dashboard payload.</summary>
    /// <returns>A <see cref="DashboardDto"/>.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<DashboardDto> Get() => await _service.GetDashboardAsync();
}
