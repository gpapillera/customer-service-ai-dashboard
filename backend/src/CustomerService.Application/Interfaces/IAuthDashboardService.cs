using CustomerService.Application.Dtos;

namespace CustomerService.Application.Interfaces;

/// <summary>Application service contract for authentication.</summary>
public interface IAuthService
{
    /// <summary>Validates credentials and returns a JWT on success.</summary>
    /// <param name="request">Login request.</param>
    /// <returns>A <see cref="LoginResponse"/> or null if invalid.</returns>
    Task<LoginResponse?> LoginAsync(LoginRequest request);
}

/// <summary>Application service contract for dashboard analytics.</summary>
public interface IDashboardService
{
    /// <summary>Builds the full dashboard payload.</summary>
    /// <returns>A <see cref="DashboardDto"/>.</returns>
    Task<DashboardDto> GetDashboardAsync();
}
